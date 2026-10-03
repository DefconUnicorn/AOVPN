using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AOVPN.Desktop;

internal static class ThemeLoader
{
    public static void Apply(Application application)
    {
        var values = Read(Path.Combine(AppContext.BaseDirectory, "style.css"));
        SetBrush(application, "WindowBrush", values, "window-background");
        SetBrush(application, "PanelBrush", values, "panel-background");
        SetBrush(application, "TextBrush", values, "text-color");
        SetBrush(application, "MutedBrush", values, "muted-text-color");
        SetBrush(application, "AccentBrush", values, "accent-color");
        SetBrush(application, "BorderBrush", values, "border-color");
        SetBrush(application, "ButtonBackgroundBrush", values, "button-background");
        SetBrush(application, "ButtonBorderBrush", values, "button-border");
        SetBrush(application, "ButtonTextBrush", values, "button-text");
        SetBrush(application, "GraphBackgroundBrush", values, "graph-background");
        SetBrush(application, "GraphBorderBrush", values, "graph-border");
        SetBrush(application, "GraphGridBrush", values, "graph-grid");
        SetBrush(application, "GraphSentBrush", values, "graph-sent");
        SetBrush(application, "GraphReceivedBrush", values, "graph-received");
        SetBrush(application, "GraphLocalSentBrush", values, "graph-local-sent");
        SetBrush(application, "GraphLocalReceivedBrush", values, "graph-local-received");
        SetBrush(application, "ToggleBackgroundBrush", values, "toggle-background");
        SetBrush(application, "ToggleActiveBrush", values, "toggle-active");
        SetBrush(application, "ToggleDisabledBrush", values, "toggle-disabled");

        if (double.TryParse(Get(values, "logo-width", "280"), NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
            application.Resources["LogoWidth"] = width;
        if (double.TryParse(Get(values, "logo-height", "105"), NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
            application.Resources["LogoHeight"] = height;

        var logoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Get(values, "logo-file", "assets/logotop.png")));
        if (File.Exists(logoPath))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(logoPath);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            application.Resources["LogoImageSource"] = image;
        }
    }

    public static Color GetColor(string key, Color fallback)
    {
        var values = Read(Path.Combine(AppContext.BaseDirectory, "style.css"));
        return ParseColor(Get(values, key, null), fallback);
    }

    private static Dictionary<string, string> Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return values;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Split("/*", 2)[0].Trim();
            if (!line.StartsWith("--") || !line.Contains(':'))
                continue;
            var parts = line.TrimEnd(';').Split(':', 2);
            values[parts[0].Trim().TrimStart('-')] = parts[1].Trim();
        }
        return values;
    }

    private static string Get(Dictionary<string, string> values, string key, string? fallback) => values.TryGetValue(key, out var value) ? value : fallback ?? string.Empty;

    private static void SetBrush(Application app, string resource, Dictionary<string, string> values, string key)
    {
        app.Resources[resource] = new SolidColorBrush(ParseColor(Get(values, key, null), Colors.White));
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value ?? string.Empty)!; }
        catch { return fallback; }
    }
}
