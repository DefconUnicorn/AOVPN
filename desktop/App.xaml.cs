using System.Windows;
using Microsoft.Win32;

namespace AOVPN.Desktop;

public partial class App : System.Windows.Application
{
    internal static TrayController? Tray { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeLoader.Apply(this);
        RegisterStartup();
        var window = new MainWindow();
        MainWindow = window;
        Tray = new TrayController(window);
        window.Show();
        window.Hide();
    }

    private static void RegisterStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
        key?.SetValue("AOVPN", $"\"{Environment.ProcessPath}\"");
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Tray?.Dispose();
        base.OnExit(e);
    }
}
