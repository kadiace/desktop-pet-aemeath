using System.IO;
using System.Windows;
using AemeathCompanion.Native;
using AemeathCompanion.UI;

namespace AemeathCompanion;

public partial class App : Application
{
    internal const string SingleInstanceMutexName = "AemeathCompanion.SingleInstance.Mutex";

    private Mutex? _singleInstanceMutex;
    private CompanionWindow? _mainWindow;
#if DEBUG
    private DevLifecycleControl? _devControl;
#endif

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        if (e.Args.Any(a => string.Equals(a, DevLifecycleControl.ShutdownArg, StringComparison.OrdinalIgnoreCase)))
        {
            DevLifecycleControl.RequestShutdownFromCli(AppContext.BaseDirectory, SingleInstanceMutexName);
            Shutdown(0);
            return;
        }
#endif

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _mainWindow = new CompanionWindow();
        MainWindow = _mainWindow;

#if DEBUG
        if (DevLifecycleControl.TryFindLocalDevRepoRoot(AppContext.BaseDirectory, out string repoRoot))
        {
            string controlDir = Path.Combine(repoRoot, DevLifecycleControl.ControlDirName);
            _devControl = new DevLifecycleControl(
                controlDir,
                onShutdownRequested: () => Dispatcher.BeginInvoke(() => _mainWindow?.RequestShutdown()),
                exePath: Environment.ProcessPath,
                baseDirectory: AppContext.BaseDirectory,
                configuration: "Debug");
            _devControl.Start();
        }
#endif

        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
#if DEBUG
        try { _devControl?.Dispose(); } catch { }
        _devControl = null;
#endif
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
