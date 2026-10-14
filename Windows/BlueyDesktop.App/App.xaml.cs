using System.Windows;

namespace BlueyDesktop.App;

/// <summary>Startup wiring: settings first, then the companion window and its tray icon.</summary>
public partial class App : Application
{
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppSettings.Load(AppContext.BaseDirectory);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        _tray = new TrayIcon(window, AppSettings.HasOpenAiKey);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
