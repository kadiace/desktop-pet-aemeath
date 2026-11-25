using System.Windows;
using AemeathCompanion.UI;

namespace AemeathCompanion;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private CompanionWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "AemeathCompanion.SingleInstance.Mutex", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _mainWindow = new CompanionWindow();
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
