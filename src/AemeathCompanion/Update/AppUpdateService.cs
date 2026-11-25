using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AemeathCompanion.Update;

public sealed record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    string? LatestTag,
    bool IsUpdateAvailable,
    bool HasDownload,
    string? DownloadUrl,
    string? Sha256,
    string? ReleaseNotes,
    string StatusText,
    bool Succeeded);

internal sealed class VersionApiResponse
{
    [JsonPropertyName("latestTag")] public string? LatestTag { get; set; }
    [JsonPropertyName("latestVersion")] public string? LatestVersion { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("hasDownload")] public bool HasDownload { get; set; }
    [JsonPropertyName("fileName")] public string? FileName { get; set; }
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; set; }
    [JsonPropertyName("portalUrl")] public string? PortalUrl { get; set; }
}

/// <summary>
/// Checks the internal LAN release server (default http://172.21.10.15:17515/api/version)
/// for newer Git-tagged versions and applies self-updating zip packages cleanly.
/// </summary>
public sealed class AppUpdateService : IDisposable
{
    public const string DefaultServerBaseUrl = "http://172.21.10.15:17515";

    private readonly HttpClient _http;
    private bool _disposed;

    public string ServerBaseUrl { get; }
    public string CurrentVersion { get; }
    public UpdateCheckResult LatestResult { get; private set; }
    public bool IsChecking { get; private set; }
    public bool IsUpdating { get; private set; }

    public event Action<UpdateCheckResult>? UpdateStatusChanged;

    public AppUpdateService(string? serverBaseUrl = null, string? currentVersionOverride = null)
    {
        string raw = string.IsNullOrWhiteSpace(serverBaseUrl) ? DefaultServerBaseUrl : serverBaseUrl.Trim();
        ServerBaseUrl = raw.TrimEnd('/');
        CurrentVersion = NormalizeVersion(currentVersionOverride ?? ReadAssemblyVersion());
        LatestResult = new UpdateCheckResult(
            CurrentVersion,
            null,
            null,
            IsUpdateAvailable: false,
            HasDownload: false,
            DownloadUrl: null,
            Sha256: null,
            ReleaseNotes: null,
            StatusText: "확인 전",
            Succeeded: false);

        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public static string ReadAssemblyVersion()
    {
        var asm = typeof(AppUpdateService).Assembly;
        string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            if (plus > 0) info = info[..plus];
            return NormalizeVersion(info);
        }

        var v = asm.GetName().Version;
        return v is not null ? $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}" : "0.0.1";
    }

    public static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "0.0.0";
        string trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];
        return trimmed;
    }

    /// <summary>
    /// Returns true if candidateVersion is strictly newer than currentVersion (SemVer aware).
    /// </summary>
    public static bool IsNewerVersion(string currentVersion, string candidateVersion)
    {
        var cur = ParseSemVer(currentVersion);
        var lat = ParseSemVer(candidateVersion);

        if (lat.Major != cur.Major) return lat.Major > cur.Major;
        if (lat.Minor != cur.Minor) return lat.Minor > cur.Minor;
        if (lat.Patch != cur.Patch) return lat.Patch > cur.Patch;

        // Release > pre-release (e.g. 1.0.0 > 1.0.0-beta.1)
        if (string.IsNullOrEmpty(lat.Pre) && !string.IsNullOrEmpty(cur.Pre)) return true;
        if (!string.IsNullOrEmpty(lat.Pre) && string.IsNullOrEmpty(cur.Pre)) return false;
        return string.Compare(lat.Pre, cur.Pre, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static (int Major, int Minor, int Patch, string Pre) ParseSemVer(string raw)
    {
        string clean = NormalizeVersion(raw);
        string core = clean;
        string pre = string.Empty;
        int dash = clean.IndexOf('-');
        if (dash >= 0)
        {
            core = clean[..dash];
            pre = clean[(dash + 1)..];
        }

        string[] parts = core.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out int m) ? m : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : 0;
        int patch = parts.Length > 2 && int.TryParse(parts[2], out int p) ? p : 0;
        return (major, minor, patch, pre);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (_disposed || IsChecking) return LatestResult;
        IsChecking = true;

        try
        {
            using var resp = await _http.GetAsync($"{ServerBaseUrl}/api/version", ct);
            if (!resp.IsSuccessStatusCode)
            {
                return PublishResult(new UpdateCheckResult(
                    CurrentVersion, null, null, false, false, null, null, null,
                    $"서버 응답 오류 ({(int)resp.StatusCode})", Succeeded: false));
            }

            string json = await resp.Content.ReadAsStringAsync(ct);
            var payload = JsonSerializer.Deserialize<VersionApiResponse>(json);
            string latestVer = NormalizeVersion(payload?.LatestVersion ?? payload?.LatestTag);
            string latestTag = !string.IsNullOrWhiteSpace(payload?.LatestTag)
                ? payload.LatestTag!.Trim()
                : $"v{latestVer}";

            if (string.IsNullOrWhiteSpace(latestVer) || latestVer == "0.0.0")
            {
                return PublishResult(new UpdateCheckResult(
                    CurrentVersion, null, null, false, false, null, null, null,
                    "버전 정보 없음", Succeeded: false));
            }

            bool newer = IsNewerVersion(CurrentVersion, latestVer);
            string status = newer ? $"{latestTag} 업데이트 가능" : "최신 버전";

            return PublishResult(new UpdateCheckResult(
                CurrentVersion,
                latestVer,
                latestTag,
                IsUpdateAvailable: newer,
                HasDownload: payload?.HasDownload ?? false,
                DownloadUrl: payload?.DownloadUrl,
                Sha256: payload?.Sha256,
                ReleaseNotes: payload?.Notes,
                StatusText: status,
                Succeeded: true));
        }
        catch (OperationCanceledException)
        {
            return PublishResult(new UpdateCheckResult(
                CurrentVersion, null, null, false, false, null, null, null,
                "서버 연결 시간 초과", Succeeded: false));
        }
        catch
        {
            return PublishResult(new UpdateCheckResult(
                CurrentVersion, null, null, false, false, null, null, null,
                "서버 미연결", Succeeded: false));
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Downloads the release zip, verifies SHA-256, extracts to temp staging, and launches a detached
    /// updater script that swaps binaries once this process exits and relaunches the updated app.
    /// Returns true when the caller should shut down immediately to complete the update.
    /// </summary>
    public async Task<(bool ShouldShutdown, string Message)> DownloadAndApplyUpdateAsync(
        UpdateCheckResult info,
        Action<int>? onProgress = null,
        CancellationToken ct = default)
    {
        if (_disposed || IsUpdating) return (false, "이미 업데이트 진행 중");

        if (!info.HasDownload || string.IsNullOrWhiteSpace(info.DownloadUrl))
        {
            OpenPortalInBrowser();
            return (false, "다운로드 페이지를 브라우저에서 열었습니다");
        }

        IsUpdating = true;
        try
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "AemeathCompanionUpdate", Guid.NewGuid().ToString("N")[..8]);
            string zipPath = Path.Combine(tempRoot, "release.zip");
            string stageDir = Path.Combine(tempRoot, "stage");
            Directory.CreateDirectory(tempRoot);

            string fullUrl = info.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? info.DownloadUrl
                : $"{ServerBaseUrl}{info.DownloadUrl}";

            using (var downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            using (var response = await downloadClient.GetAsync(fullUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;
                await using var netStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = File.Create(zipPath);

                byte[] buffer = new byte[65536];
                long readTotal = 0;
                int lastPct = -1;
                int bytesRead;
                while ((bytesRead = await netStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    readTotal += bytesRead;
                    if (totalBytes is > 0)
                    {
                        int pct = (int)Math.Clamp(readTotal * 100 / totalBytes.Value, 0, 100);
                        if (pct != lastPct)
                        {
                            lastPct = pct;
                            onProgress?.Invoke(pct);
                        }
                    }
                }
            }

            // Optional SHA-256 verification
            if (!string.IsNullOrWhiteSpace(info.Sha256))
            {
                await using var verifyStream = File.OpenRead(zipPath);
                byte[] hashBytes = await SHA256.HashDataAsync(verifyStream, ct);
                string actualHash = Convert.ToHexString(hashBytes);
                if (!actualHash.Equals(info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "파일 무결성(SHA-256) 검증 실패");
                }
            }

            ZipFile.ExtractToDirectory(zipPath, stageDir, overwriteFiles: true);
            string payloadDir = ResolveExtractedAppRoot(stageDir);
            string targetDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string exeName = Path.GetFileName(Environment.ProcessPath ?? "AemeathCompanion.exe");

            if (!File.Exists(Path.Combine(payloadDir, exeName)))
            {
                return (false, "압축 파일에 실행 파일이 없습니다");
            }

            string scriptPath = Path.Combine(tempRoot, "apply_update.ps1");
            int currentPid = Environment.ProcessId;
            string script = $$"""
                $ErrorActionPreference = 'SilentlyContinue'
                $pidToWait = {{currentPid}}
                for ($i = 0; $i -lt 60; $i++) {
                    if (-not (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) { break }
                    Start-Sleep -Milliseconds 250
                }
                Start-Sleep -Milliseconds 250
                Copy-Item -Path "{{EscapePs(payloadDir)}}\*" -Destination "{{EscapePs(targetDir)}}" -Recurse -Force
                Start-Process -FilePath "{{EscapePs(Path.Combine(targetDir, exeName))}}" -WorkingDirectory "{{EscapePs(targetDir)}}"
                Start-Sleep -Seconds 2
                Remove-Item -Path "{{EscapePs(tempRoot)}}" -Recurse -Force
                """;
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            return (true, "업데이트 적용을 위해 재시작합니다...");
        }
        catch (Exception ex)
        {
            return (false, $"업데이트 실패: {ex.Message}");
        }
        finally
        {
            IsUpdating = false;
        }
    }

    public void OpenPortalInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"{ServerBaseUrl}/",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static string ResolveExtractedAppRoot(string stageDir)
    {
        if (File.Exists(Path.Combine(stageDir, "AemeathCompanion.exe")))
            return stageDir;

        var dirs = Directory.GetDirectories(stageDir);
        if (dirs.Length == 1 && File.Exists(Path.Combine(dirs[0], "AemeathCompanion.exe")))
            return dirs[0];

        return stageDir;
    }

    private static string EscapePs(string path) => path.Replace("\"", "`\"");

    private UpdateCheckResult PublishResult(UpdateCheckResult result)
    {
        LatestResult = result;
        UpdateStatusChanged?.Invoke(result);
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
