using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace AOVPN.Desktop;

internal sealed class TrayController : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Dictionary<string, Icon> _icons = new(StringComparer.OrdinalIgnoreCase);

    public TrayController(MainWindow window)
    {
        _window = window;
        _statusItem = new Forms.ToolStripMenuItem("VPN: disconnected") { Enabled = false };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open AOVPN", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => _window.ExitApplication());
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "AOVPN",
            Icon = GetIcon("idle"),
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    public void Update(string vpnState, string serviceState)
    {
        var key = vpnState.Equals("connected", StringComparison.OrdinalIgnoreCase)
            ? "connected"
            : vpnState.Equals("connecting", StringComparison.OrdinalIgnoreCase)
                ? "connecting"
                : "idle";
        _notifyIcon.Icon = GetIcon(key);
        _notifyIcon.Text = $"AOVPN: VPN {vpnState}; service {serviceState}"[..Math.Min(63, $"AOVPN: VPN {vpnState}; service {serviceState}".Length)];
        _statusItem.Text = $"VPN: {vpnState} | Service: {serviceState}";
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private Icon GetIcon(string state)
    {
        if (_icons.TryGetValue(state, out var icon))
            return icon;
        var path = Path.Combine(AppContext.BaseDirectory, $"aovpn-systray-icon-{state}.png");
        if (File.Exists(path))
        {
            using var bitmap = new Bitmap(path);
            using var sourceIcon = Icon.FromHandle(bitmap.GetHicon());
            icon = (Icon)sourceIcon.Clone();
        }
        else
        {
            var color = state switch
            {
                "connected" => Color.FromArgb(70, 210, 155),
                "connecting" => Color.FromArgb(245, 180, 70),
                _ => Color.FromArgb(145, 155, 170)
            };
            using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(color);
                graphics.FillEllipse(brush, 3, 3, 26, 26);
            }
            using var sourceIcon = Icon.FromHandle(bitmap.GetHicon());
            icon = (Icon)sourceIcon.Clone();
        }
        _icons[state] = icon;
        return icon;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        foreach (var icon in _icons.Values)
            icon.Dispose();
    }
}
