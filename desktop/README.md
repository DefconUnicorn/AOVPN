# AOVPN Desktop

WPF desktop client for the AOVPN controller.

## Run from source

```powershell
dotnet run --project .\AOVPN.Desktop.csproj
```

## Publish

```powershell
dotnet publish .\AOVPN.Desktop.csproj -c Release --self-contained false
```

The published executable is placed under `bin\Release\net7.0-windows\win-x64\publish`.

OpenVPN is launched with `--windows-driver dco`; TAP and Wintun fallback modes are not supported.

Use **Import .ovpn** to install a profile, configure the internal-only DNS hostname, optionally configure the expected IP address, select the OpenVPN executable, and save settings. The auto-connect checkbox can be changed at any time and takes effect on the next status check.

## Theme and Logo

The published application loads `style.css` from the same folder as `AOVPN.Desktop.exe` at startup. Edit the CSS-style variables and restart AOVPN to apply changes. The default values are the built-in AMG theme.

Supported variables include:

```css
--window-background: #2D2D2D;
--panel-background: #383838;
--text-color: #FFFFFF;
--muted-text-color: #C8C8C8;
--accent-color: #62E0B5;
--border-color: #999999;
--button-background: #263748;
--button-border: #42566B;
--button-text: #F1F5F9;
--graph-background: #2D2D2D;
--graph-border: #2D2D2D;
--graph-grid: #3C3C3C;
--graph-sent: #62E0B5;
--graph-received: #8AB4F8;
--graph-local-sent: #F6C85F;
--graph-local-received: #FF8A65;
--toggle-background: #3B4B5D;
--toggle-active: #62E0B5;
--toggle-disabled: #6B7280;
--logo-file: assets/logotop.png;
--logo-width: 280;
--logo-height: 105;
--font-family: Segoe UI;
```

The default top logo is `assets\logotop.png`. Replace it with a company logo of your choice, or update `--logo-file`; the configured width and height are applied at startup.
