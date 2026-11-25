using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AemeathCompanion.Native;

public sealed record DevInstanceInfo(
    int Pid,
    DateTime StartedAtUtc,
    string ExePath,
    string BaseDirectory,
    string Configuration);

/// <summary>
/// Local-development-only lifecycle controller that lets local dev scripts or coding agents
/// gracefully shut down a running Debug instance before rebuilding and relaunching.
/// Wired only under #if DEBUG and gated by <see cref="TryFindLocalDevRepoRoot"/>.
/// </summary>
public sealed class DevLifecycleControl : IDisposable
{
    public const string ShutdownArg = "--shutdown";
    public const string ShutdownEventName = @"Local\AemeathCompanion.Dev.Shutdown";
    public const string ControlDirName = ".dev-control";
    public const string InstanceFileName = "instance.json";
    public const string ShutdownSignalFileName = "shutdown.signal";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _controlDirectory;
    private readonly string _instanceFilePath;
    private readonly string _signalFilePath;
    private readonly Action _onShutdownRequested;
    private readonly int _currentPid;
    private readonly bool _enableNamedEvent;
    private readonly string _eventName;
    private readonly string _exePath;
    private readonly string _baseDirectory;
    private readonly string _configuration;

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _pollTimer;
    private EventWaitHandle? _shutdownEvent;
    private ManualResetEventSlim? _disposeEvent;
    private Thread? _eventThread;
    private string? _ignoredStaleSignalContent;
    private int _triggered;
    private int _disposed;

    public DevLifecycleControl(
        string controlDirectory,
        Action onShutdownRequested,
        int? currentPid = null,
        bool enableNamedEvent = true,
        string? eventName = null,
        string? exePath = null,
        string? baseDirectory = null,
        string? configuration = "Debug")
    {
        _controlDirectory = controlDirectory;
        _instanceFilePath = Path.Combine(controlDirectory, InstanceFileName);
        _signalFilePath = Path.Combine(controlDirectory, ShutdownSignalFileName);
        _onShutdownRequested = onShutdownRequested;
        _currentPid = currentPid ?? Environment.ProcessId;
        _enableNamedEvent = enableNamedEvent;
        _eventName = string.IsNullOrWhiteSpace(eventName) ? ShutdownEventName : eventName;
        _exePath = exePath ?? Environment.ProcessPath ?? string.Empty;
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _configuration = string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration;
    }

    public string InstanceFilePath => _instanceFilePath;
    public string SignalFilePath => _signalFilePath;

    /// <summary>
    /// Returns true only when <paramref name="baseDirectory"/> is inside a local repository's
    /// build output folder (<c>.../bin/...</c>) whose ancestor contains <c>src/AemeathCompanion/AemeathCompanion.csproj</c>.
    /// Prevents activation in deployed/extracted installations even if built in Debug.
    /// </summary>
    public static bool TryFindLocalDevRepoRoot(string? baseDirectory, out string repoRoot)
    {
        repoRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return false;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        string[] segments = normalized.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        bool underBin = segments.Any(s => string.Equals(s, "bin", StringComparison.OrdinalIgnoreCase));
        if (!underBin)
            return false;

        DirectoryInfo? current = new(normalized);
        for (int depth = 0; depth < 8 && current is not null; depth++)
        {
            string expectedCsproj = Path.Combine(current.FullName, "src", "AemeathCompanion", "AemeathCompanion.csproj");
            if (File.Exists(expectedCsproj))
            {
                repoRoot = current.FullName;
                return true;
            }
            current = current.Parent;
        }

        return false;
    }

    public static bool IsLocalDevDirectory(string? baseDirectory)
        => TryFindLocalDevRepoRoot(baseDirectory, out _);

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        Directory.CreateDirectory(_controlDirectory);
        ClearOrRecordStaleSignal();
        WriteInstanceInfo();

        try
        {
            _watcher = new FileSystemWatcher(_controlDirectory, ShutdownSignalFileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnSignalFileChanged;
            _watcher.Changed += OnSignalFileChanged;
            _watcher.Renamed += OnSignalFileChanged;
        }
        catch
        {
            // Fallback polling timer still guarantees delivery
        }

        _pollTimer = new System.Threading.Timer(_ => CheckShutdownSignalFile(), null, 200, 250);

        if (_enableNamedEvent)
        {
            try
            {
                _disposeEvent = new ManualResetEventSlim(false);
                _shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
                _eventThread = new Thread(WaitForNamedEventLoop)
                {
                    Name = "DevLifecycleControl.EventWaiter",
                    IsBackground = true
                };
                _eventThread.Start();
            }
            catch
            {
                // File-based signal still works even if named kernel event is unavailable
            }
        }
    }

    /// <summary>
    /// Requests shutdown of any running local dev instance via both named event and signal file,
    /// then waits briefly for the single-instance mutex to be released.
    /// </summary>
    public static bool RequestShutdownFromCli(
        string baseDirectory,
        string singleInstanceMutexName,
        int waitTimeoutMs = 5000,
        string? eventName = null)
    {
        if (!TryFindLocalDevRepoRoot(baseDirectory, out string repoRoot))
            return false;

        string targetEvent = string.IsNullOrWhiteSpace(eventName) ? ShutdownEventName : eventName;
        try
        {
            if (EventWaitHandle.TryOpenExisting(targetEvent, out EventWaitHandle? existing))
            {
                using (existing)
                {
                    existing.Set();
                }
            }
        }
        catch
        {
            // Named event may not exist across session boundaries; file signal handles it
        }

        string controlDir = Path.Combine(repoRoot, ControlDirName);
        string signalPath = Path.Combine(controlDir, ShutdownSignalFileName);
        string instancePath = Path.Combine(controlDir, InstanceFileName);

        try
        {
            Directory.CreateDirectory(controlDir);
            File.WriteAllText(signalPath, $"shutdown {DateTime.UtcNow:O} {Guid.NewGuid():N}");
        }
        catch
        {
            // Ignore write errors
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < waitTimeoutMs)
        {
            bool mutexFree = TryCheckMutexFree(singleInstanceMutexName);
            bool instanceGone = !File.Exists(instancePath);
            if (mutexFree && instanceGone)
                return true;

            Thread.Sleep(100);
        }

        return TryCheckMutexFree(singleInstanceMutexName);
    }

    private static bool TryCheckMutexFree(string mutexName)
    {
        try
        {
            using var mutex = new Mutex(true, mutexName, out bool createdNew);
            if (createdNew)
            {
                try { mutex.ReleaseMutex(); } catch { }
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void ClearOrRecordStaleSignal()
    {
        if (!File.Exists(_signalFilePath))
            return;

        try
        {
            File.Delete(_signalFilePath);
            _ignoredStaleSignalContent = null;
        }
        catch
        {
            _ignoredStaleSignalContent = TryReadSignalText();
        }
    }

    private void WriteInstanceInfo()
    {
        try
        {
            var info = new DevInstanceInfo(
                Pid: _currentPid,
                StartedAtUtc: DateTime.UtcNow,
                ExePath: _exePath,
                BaseDirectory: _baseDirectory,
                Configuration: _configuration);
            string json = JsonSerializer.Serialize(info, JsonOptions);
            File.WriteAllText(_instanceFilePath, json);
        }
        catch
        {
            // Non-fatal in dev helper
        }
    }

    private void OnSignalFileChanged(object sender, FileSystemEventArgs e)
    {
        CheckShutdownSignalFile();
    }

    internal void CheckShutdownSignalFile()
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _triggered) != 0)
            return;

        if (!File.Exists(_signalFilePath))
            return;

        string? content = TryReadSignalText();
        if (content is null)
            return;

        if (_ignoredStaleSignalContent is not null &&
            string.Equals(content, _ignoredStaleSignalContent, StringComparison.Ordinal))
        {
            return;
        }

        if (!MatchesTargetPid(content, _currentPid))
            return;

        try { File.Delete(_signalFilePath); } catch { }
        TriggerShutdownOnce();
    }

    public static bool MatchesTargetPid(string content, int currentPid)
    {
        string trimmed = content.Trim();
        if (string.IsNullOrEmpty(trimmed) ||
            string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (int.TryParse(trimmed, out int explicitPid))
            return explicitPid == currentPid;

        if (trimmed.StartsWith("pid:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(trimmed[4..].Trim(), out int prefixedPid))
        {
            return prefixedPid == currentPid;
        }

        return true;
    }

    private string? TryReadSignalText()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var fs = new FileStream(_signalFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException)
            {
                Thread.Sleep(25);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private void WaitForNamedEventLoop()
    {
        try
        {
            if (_shutdownEvent is null || _disposeEvent is null)
                return;

            WaitHandle[] handles = [_shutdownEvent, _disposeEvent.WaitHandle];
            int idx = WaitHandle.WaitAny(handles);
            if (idx == 0 && Volatile.Read(ref _disposed) == 0)
            {
                TriggerShutdownOnce();
            }
        }
        catch
        {
            // Ignore handle teardown exceptions
        }
    }

    private void TriggerShutdownOnce()
    {
        if (Interlocked.Exchange(ref _triggered, 1) != 0)
            return;

        try
        {
            _onShutdownRequested();
        }
        catch
        {
            // Ignore shutdown callback exceptions
        }
    }

    private void CleanupInstanceFileIfOwned()
    {
        try
        {
            if (!File.Exists(_instanceFilePath))
                return;

            string json = File.ReadAllText(_instanceFilePath);
            var info = JsonSerializer.Deserialize<DevInstanceInfo>(json, JsonOptions);
            if (info is null || info.Pid == _currentPid)
            {
                File.Delete(_instanceFilePath);
            }
        }
        catch
        {
            // Ignore cleanup errors on exit
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try { _pollTimer?.Dispose(); } catch { }
        _pollTimer = null;

        if (_watcher is not null)
        {
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnSignalFileChanged;
                _watcher.Changed -= OnSignalFileChanged;
                _watcher.Renamed -= OnSignalFileChanged;
                _watcher.Dispose();
            }
            catch { }
            _watcher = null;
        }

        try { _disposeEvent?.Set(); } catch { }
        try { _eventThread?.Join(300); } catch { }
        _eventThread = null;

        try { _shutdownEvent?.Dispose(); } catch { }
        _shutdownEvent = null;

        try { _disposeEvent?.Dispose(); } catch { }
        _disposeEvent = null;

        CleanupInstanceFileIfOwned();
    }
}
