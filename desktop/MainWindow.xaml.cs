using AOVPN.Controller;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;

namespace AOVPN.Desktop;

public partial class MainWindow : Window
{
    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly OpenVpnController _vpn;
    private readonly DispatcherTimer _timer;
    private readonly Queue<TrafficSample> _trafficSamples = new();
    private bool _refreshing;
    private bool _updatingToggles;
    private bool _serviceVpnConnected;
    private bool _autoCredentialPromptShown;
    private bool _userRequestedDisconnect;
    private bool? _lastInternalNetwork;
    private TrafficSource _trafficSource;
    private bool _allowClose;

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        DashboardPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        DashboardPanel.Visibility = Visibility.Visible;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveElement(e.OriginalSource as DependencyObject))
            return;
        DragMove();
    }

    private static bool IsInteractiveElement(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase or TextBoxBase or PasswordBox)
                return true;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
        _paths = AppPaths.Create();
        _settings = SettingsStore.LoadAsync(_paths).ConfigureAwait(false).GetAwaiter().GetResult();
        _vpn = new OpenVpnController(_paths, _settings);
        LoadControls();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshStatusAsync();
        _timer.Start();
        Loaded += async (_, _) =>
        {
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            await RefreshStatusAsync();
        };
    }

    private void LoadControls()
    {
        DnsHostTextBox.Text = _settings.InternalDnsHost ?? string.Empty;
        ExpectedIpTextBox.Text = _settings.ExpectedInternalIp ?? string.Empty;
        IntervalTextBox.Text = _settings.DetectionIntervalSeconds.ToString();
        AutoConnectCheckBox.IsChecked = _settings.AutoConnect;
        OpenVpnTextBox.Text = OpenVpnLocator.Find(_settings.OpenVpnExecutable) ?? _settings.OpenVpnExecutable;
        ProfileText.Text = $"Pre-login: {_settings.PreLoginProfileName ?? "not configured"}   |   Post-login: {_settings.PostLoginProfileName ?? "not configured"}";
        ViewConfigButton.IsEnabled = _settings.PostLoginProfileId is not null || _settings.ProfileId is not null;
        ViewLogButton.IsEnabled = ViewConfigButton.IsEnabled;
        ViewServiceLogButton.IsEnabled = _settings.PreLoginProfileId is not null;
        _updatingToggles = true;
        var serviceInstalled = ServiceInstaller.IsInstalled();
        ServiceToggle.IsChecked = serviceInstalled;
        ServiceStateText.Text = $"Service: {(serviceInstalled ? ServiceInstaller.GetStatus() : "not installed")}";
        _updatingToggles = false;
    }

    private async void ImportPreLogin_Click(object sender, RoutedEventArgs e) => await ImportProfileAsync(true);
    private async void ImportPostLogin_Click(object sender, RoutedEventArgs e) => await ImportProfileAsync(false);

    private async Task ImportProfileAsync(bool preLogin)
    {
        var dialog = new OpenFileDialog { Filter = "OpenVPN profiles (*.ovpn)|*.ovpn" };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var profileName = PromptProfileName(Path.GetFileNameWithoutExtension(dialog.FileName));
            if (profileName is null)
                return;
            var profile = await ProfileImporter.ImportAsync(dialog.FileName, _paths, profileName);
            if (preLogin)
            {
                _settings.PreLoginProfileId = profile.Id;
                _settings.PreLoginProfileName = profile.Name;
            }
            else
            {
                _settings.PostLoginProfileId = profile.Id;
                _settings.PostLoginProfileName = profile.Name;
                _settings.ProfileId = profile.Id;
                _settings.ProfileName = profile.Name;
            }
            ProfileText.Text = $"Pre-login: {_settings.PreLoginProfileName ?? "not configured"}   |   Post-login: {_settings.PostLoginProfileName ?? "not configured"}";
            ViewConfigButton.IsEnabled = true;
            ViewLogButton.IsEnabled = true;
            ViewServiceLogButton.IsEnabled = _settings.PreLoginProfileId is not null;
            await SettingsStore.SaveAsync(_paths, _settings);
            SetMessage("Profile imported.");
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message);
        }
    }

    private void DeleteConfigs_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "Delete both imported pre-login and post-login profiles?", "Delete configurations", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;
        foreach (var id in new[] { _settings.PreLoginProfileId, _settings.PostLoginProfileId, _settings.ProfileId }.Where(id => id is not null).Distinct())
            Directory.Delete(Path.Combine(_paths.Profiles, id!), true);
        _settings.PreLoginProfileId = null;
        _settings.PreLoginProfileName = null;
        _settings.PostLoginProfileId = null;
        _settings.PostLoginProfileName = null;
        _settings.ProfileId = null;
        _settings.ProfileName = null;
        SettingsStore.SaveAsync(_paths, _settings).GetAwaiter().GetResult();
        ProfileText.Text = "Pre-login: not configured   |   Post-login: not configured";
        ViewConfigButton.IsEnabled = false;
        ViewLogButton.IsEnabled = false;
        SetMessage("Imported configurations deleted.");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyControlsToSettings();
            await SettingsStore.SaveAsync(_paths, _settings);
            SetMessage("Settings saved.");
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message);
        }
    }

    private async Task InstallServiceAsync()
    {
        try
        {
            ApplyControlsToSettings();
            await SettingsStore.SaveAsync(_paths, _settings);
            var machinePaths = AppPaths.Create(machineWide: true);
            var serviceExecutable = ServiceInstaller.FindServiceExecutable();
            if (serviceExecutable is null)
                throw new FileNotFoundException("Build and publish AOVPN.Service before installing it.");
            ServiceInstaller.Install(serviceExecutable, _paths.Root, machinePaths.Root);
            SetMessage("AOVPN service installed and started.");
        }
        catch (Exception ex)
        {
            SetMessage($"Service install failed: {ex.Message}");
        }
    }

    private void RemoveService()
    {
        try
        {
            ServiceInstaller.Remove();
            SetMessage("AOVPN service removed.");
        }
        catch (Exception ex)
        {
            SetMessage($"Service removal failed: {ex.Message}");
        }
    }

    private async Task ConnectAsyncFromUi()
    {
        try
        {
            _userRequestedDisconnect = false;
            if (!string.IsNullOrWhiteSpace(_settings.InternalDnsHost)
                && await NetworkDetector.IsInternalAsync(_settings, CancellationToken.None))
            {
                _updatingToggles = true;
                ConnectToggle.IsChecked = false;
                _updatingToggles = false;
                SetMessage("Connection blocked: the system is on the internal network.");
                await RefreshStatusAsync();
                return;
            }
            var credentials = RequiresCredentials()
                ? GetCredentials()
                : null;
            if (RequiresCredentials() && credentials is null)
                return;
            if (ServiceInstaller.IsInstalled())
            {
                _serviceVpnConnected = await ServiceHandoff.ConnectPostAsync(credentials?.Username ?? string.Empty, credentials?.Password ?? string.Empty);
                SetMessage(_serviceVpnConnected
                    ? "Post-login VPN connected through the AOVPN service."
                    : "Post-login VPN authentication failed; the pre-login tunnel was retained.");
            }
            else
            {
                await _vpn.ConnectAsync(credentials?.Username, credentials?.Password);
                SetMessage(await _vpn.WaitForConnectedAsync(TimeSpan.FromSeconds(30))
                    ? "Post-login VPN connected."
                    : "Post-login VPN has not completed authentication.");
            }
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message);
        }
    }

    private async Task DisconnectAsyncFromUi(bool manual = true)
    {
        if (manual)
            _userRequestedDisconnect = true;
        if (ServiceInstaller.IsInstalled())
        {
            _ = await ServiceHandoff.SendAsync("disconnect-all");
            _serviceVpnConnected = false;
        }
        else
        {
            await _vpn.DisconnectAsync();
        }
        await RefreshStatusAsync();
    }

    private async void ConnectToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_updatingToggles)
            await ConnectAsyncFromUi();
    }

    private async void ConnectToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_updatingToggles)
            return;
        try
        {
            await DisconnectAsyncFromUi();
        }
        catch (Exception ex)
        {
            SetMessage($"VPN disconnect failed: {ex.Message}");
        }
    }

    private async void ServiceToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_updatingToggles)
            await InstallServiceAsync();
    }

    private async void ServiceToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_updatingToggles)
        {
            var vpnStatus = await ServiceHandoff.GetStatusAsync();
            if (vpnStatus is "PRE_CONNECTED" or "PRE_CONNECTING" or "POST_CONNECTED" or "POST_CONNECTING" or "CONNECTED_INTERFACE"
                || HasActiveVpnInterface())
            {
                _updatingToggles = true;
                ServiceToggle.IsChecked = true;
                _updatingToggles = false;
                const string message = "Disconnect the VPN before removing the AOVPN service. The service currently owns an active VPN tunnel.";
                SetMessage(message);
                MessageBox.Show(this, message, "AOVPN service", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RemoveService();
        }
    }

    private void ViewConfig_Click(object sender, RoutedEventArgs e)
    {
        ShowTextWindow("Current OpenVPN configuration", _vpn.CurrentConfigPath is { } path && File.Exists(path)
            ? File.ReadAllText(path)
            : "No imported OpenVPN configuration is available.");
    }

    private void ViewLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = new[] { _vpn.CurrentLogPath, FindLatestMachineLog(preLogin: false) }
                .Where(path => path is not null && File.Exists(path))
                .Select(path => path!)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            var content = logPath is not null
                ? File.ReadAllText(logPath)
                : "No connection log has been created yet.";
            ShowTextWindow("OpenVPN connection log", content);
        }
        catch (Exception ex)
        {
            SetMessage($"Could not read connection log: {ex.Message}");
        }
    }

    private void ViewServiceLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = FindLatestMachineLog(preLogin: true);
            ShowTextWindow("AOVPN service log", path is not null && File.Exists(path)
                ? File.ReadAllText(path)
                : "No service connection log has been created yet.");
        }
        catch (Exception ex)
        {
            SetMessage($"Could not read service log: {ex.Message}");
        }
    }

    private string? FindLatestMachineLog(bool preLogin)
    {
        var id = preLogin ? _settings.PreLoginProfileId : (_settings.PostLoginProfileId ?? _settings.ProfileId);
        if (id is null)
            return null;
        var directory = Path.Combine(AppPaths.Create(machineWide: true).Profiles, id);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "connection-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
    }

    private void ViewNetwork_Click(object sender, RoutedEventArgs e)
    {
        ShowTextWindow("Network details", BuildNetworkDetails());
    }

    private bool RequiresCredentials()
    {
        var path = _vpn.CurrentConfigPath;
        if (path is null || !File.Exists(path))
            return false;
        return File.ReadLines(path).Any(line =>
        {
            var value = line.Trim();
            if (value.StartsWith("#") || value.StartsWith(";"))
                return false;
            var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1 && parts[0].Equals("auth-user-pass", StringComparison.OrdinalIgnoreCase);
        });
    }

    private CredentialInput? GetCredentials()
    {
        var stored = CredentialStore.Read();
        if (stored is not null)
            return new CredentialInput(stored.Value.Username, stored.Value.Password, false);
        var entered = PromptCredentials();
        if (entered is { Save: true })
            CredentialStore.Write(entered.Value.Username, entered.Value.Password);
        return entered;
    }

    private CredentialInput? PromptCredentials()
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Username", Margin = new Thickness(0, 0, 0, 5) });
        var username = new TextBox { Width = 390, Padding = new Thickness(8, 6, 8, 6) };
        panel.Children.Add(username);
        panel.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 14, 0, 5) });
        var password = new PasswordBox { Width = 390, Padding = new Thickness(8, 6, 8, 6) };
        panel.Children.Add(password);
        var save = new CheckBox { Content = "Save credentials in Windows Credential Manager", Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(save);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Connect", IsDefault = true };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Owner = this,
            Title = "OpenVPN credentials",
            Content = panel,
            Width = 470,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("WindowBrush")
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() == true ? new CredentialInput(username.Text, password.Password, save.IsChecked == true) : null;
    }

    private string? PromptProfileName(string defaultName)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Profile name", Margin = new Thickness(0, 0, 0, 5) });
        var name = new TextBox { Text = defaultName, MinWidth = 320 };
        panel.Children.Add(name);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Import", IsDefault = true };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Owner = this,
            Title = "Name OpenVPN profile",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("WindowBrush")
        };
        ok.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text))
                dialog.DialogResult = true;
        };
        return dialog.ShowDialog() == true ? name.Text.Trim() : null;
    }

    private void ShowTextWindow(string title, string content)
    {
        var viewer = new Window
        {
            Owner = this,
            Title = title,
            Width = 900,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("WindowBrush")
        };
        viewer.Content = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(14),
            FontFamily = new FontFamily("Consolas")
        };
        viewer.Show();
    }

    private void BrowseOpenVpn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "OpenVPN executable (openvpn.exe)|openvpn.exe|Executable files (*.exe)|*.exe" };
        if (dialog.ShowDialog() == true)
            OpenVpnTextBox.Text = dialog.FileName;
    }

    private void ApplyControlsToSettings()
    {
        _settings.InternalDnsHost = EmptyAsNull(DnsHostTextBox.Text);
        _settings.ExpectedInternalIp = EmptyAsNull(ExpectedIpTextBox.Text);
        _settings.DetectionIntervalSeconds = Math.Clamp(int.Parse(IntervalTextBox.Text), 5, 3600);
        _settings.AutoConnect = AutoConnectCheckBox.IsChecked == true;
        _settings.OpenVpnExecutable = OpenVpnTextBox.Text.Trim();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private async Task RefreshStatusAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var isInternal = !string.IsNullOrWhiteSpace(_settings.InternalDnsHost)
                && await NetworkDetector.IsInternalAsync(_settings, CancellationToken.None);
            if (_lastInternalNetwork == true && !isInternal)
            {
                _autoCredentialPromptShown = false;
                _serviceVpnConnected = false;
            }
            _lastInternalNetwork = isInternal;
            NetworkStateText.Text = isInternal ? "Internal" : "External";
            if (!isInternal && _settings.AutoConnect && _userRequestedDisconnect)
            {
                _userRequestedDisconnect = false;
                _autoCredentialPromptShown = false;
            }
            NetworkStatusText.Text = $"Network: {(isInternal ? "internal" : "external")}";
            var serviceVpnStatus = ServiceInstaller.IsInstalled()
                ? await ServiceHandoff.GetStatusAsync()
                : "UNAVAILABLE";
            if (serviceVpnStatus == "UNAVAILABLE" && !_userRequestedDisconnect && HasActiveVpnInterface())
                serviceVpnStatus = "CONNECTED_INTERFACE";
            var servicePreVpn = serviceVpnStatus == "PRE_CONNECTED" || serviceVpnStatus == "PRE_CONNECTING";
            var serviceVpnConnected = serviceVpnStatus is "PRE_CONNECTED" or "POST_CONNECTED" or "CONNECTED_INTERFACE";
            var serviceVpnRunning = serviceVpnStatus is "PRE_CONNECTED" or "POST_CONNECTED" or "PRE_CONNECTING" or "POST_CONNECTING" or "CONNECTED_INTERFACE";
            var vpnConnected = serviceVpnConnected || _serviceVpnConnected || _vpn.IsConnected;
            var vpnRunning = serviceVpnRunning || _serviceVpnConnected || _vpn.IsRunning;
            if (serviceVpnStatus == "POST_CONNECTED" || serviceVpnStatus == "POST_CONNECTING")
            {
                _serviceVpnConnected = serviceVpnStatus == "POST_CONNECTED";
                vpnConnected = _serviceVpnConnected;
                vpnRunning = true;
                VpnStatusText.Text = $"VPN: {serviceVpnStatus.Replace("_", " ").ToLowerInvariant()}";
            }
            else if (servicePreVpn)
            {
                vpnConnected = serviceVpnStatus == "PRE_CONNECTED";
                vpnRunning = true;
            }
            VpnStatusText.Text = $"VPN: {(vpnConnected ? "connected" : vpnRunning ? "connecting" : "disconnected")}";
            _updatingToggles = true;
            ConnectToggle.IsEnabled = true;
            ConnectToggle.IsChecked = vpnRunning;
            ConnectToggle.ToolTip = isInternal ? "VPN connection is disabled on the internal network." : null;
            var serviceInstalled = ServiceInstaller.IsInstalled();
            ServiceToggle.IsChecked = serviceInstalled;
            var serviceState = serviceInstalled ? ServiceInstaller.GetStatus() : "not installed";
            var serviceVpnLabel = serviceVpnStatus switch
            {
                "PRE_CONNECTED" => "pre-login connected",
                "PRE_CONNECTING" => "pre-login connecting",
                "POST_CONNECTED" => "post-login connected",
                "POST_CONNECTING" => "post-login connecting",
                "CONNECTED_INTERFACE" => "connected",
                "DISCONNECTED" => "idle",
                "UNAVAILABLE" => "status unavailable",
                _ => serviceVpnStatus.ToLowerInvariant().Replace('_', ' ')
            };
            ServiceStateText.Text = $"Service: {serviceState} | VPN: {serviceVpnLabel}";
            DashboardServiceInstallText.Text = serviceInstalled ? "INSTALLED" : "NOT INSTALLED";
            DashboardServiceConnectionText.Text = !serviceInstalled ? "N/A" : serviceVpnStatus switch
            {
                "PRE_CONNECTED" or "POST_CONNECTED" => "CONNECTED",
                "PRE_CONNECTING" or "POST_CONNECTING" => "CONNECTING",
                _ => "IDLE"
            };
            DashboardVpnProgramText.Text = vpnRunning ? "RUNNING" : "STOPPED";
            DashboardVpnConnectionText.Text = vpnConnected ? "CONNECTED" : vpnRunning ? "CONNECTING" : "DISCONNECTED";
            var userVpnState = serviceVpnStatus is "POST_CONNECTED" or "CONNECTED_INTERFACE" || _serviceVpnConnected || _vpn.IsConnected
                ? "connected"
                : serviceVpnStatus == "POST_CONNECTING" || _vpn.IsRunning
                    ? "connecting"
                    : "idle";
            App.Tray?.Update(userVpnState, serviceState);
            _updatingToggles = false;

            if (isInternal)
                await DisconnectAsyncFromUi(manual: false);
            else if (_settings.AutoConnect && !_vpn.IsRunning && (_settings.PostLoginProfileId ?? _settings.ProfileId) is not null)
            {
                if (servicePreVpn)
                    SetMessage("Pre-login service VPN is active. Use Connect to enter post-login credentials and hand off.");
                else if (RequiresCredentials())
                {
                    if (!_autoCredentialPromptShown)
                    {
                        _autoCredentialPromptShown = true;
                        var credentials = GetCredentials();
                        if (credentials is not null)
                        {
                            if (ServiceInstaller.IsInstalled())
                                _serviceVpnConnected = await ServiceHandoff.ConnectPostAsync(credentials.Value.Username, credentials.Value.Password);
                            else
                            {
                                await _vpn.ConnectAsync(credentials.Value.Username, credentials.Value.Password);
                                await _vpn.WaitForConnectedAsync(TimeSpan.FromSeconds(30));
                            }
                            SetMessage(_serviceVpnConnected || _vpn.IsConnected
                                ? "Automatic post-login VPN connection started."
                                : "Automatic post-login VPN connection failed.");
                        }
                    }
                }
                else
                    await _vpn.ConnectAsync();
            }

            UpdateTrafficGraph(isInternal, vpnConnected);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private string BuildNetworkDetails()
    {
        var output = new StringBuilder();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(item => item.OperationalStatus == OperationalStatus.Up
                         && item.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var properties = adapter.GetIPProperties();
            var addresses = properties.UnicastAddresses
                .Select(address => address.Address)
                .Where(address => !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .ToArray();
            if (addresses.Length == 0)
                continue;

            var vpn = IsVpnInterface(adapter);
            output.Append(vpn ? "VPN  " : "LAN  ")
                .Append(adapter.Name)
                .Append(" [")
                .Append(adapter.Description)
                .AppendLine("]")
                .Append("     IP: ")
                .AppendLine(string.Join(", ", addresses));

            var gateways = properties.GatewayAddresses
                .Select(gateway => gateway.Address.ToString())
                .ToArray();
            if (gateways.Length > 0)
                output.Append("     Gateway: ").AppendLine(string.Join(", ", gateways));
        }

        output.AppendLine().AppendLine("IPv4 routing table");
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "route.exe",
                Arguments = "print -4",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is not null)
            {
                output.AppendLine(process.StandardOutput.ReadToEnd());
                process.WaitForExit();
            }
        }
        catch (Exception ex)
        {
            output.AppendLine($"Unable to read routes: {ex.Message}");
        }

        return output.Length == 0 ? "No active network interfaces detected." : output.ToString().TrimEnd();
    }

    private void UpdateTrafficGraph(bool isInternal, bool vpnConnected)
    {
        var source = isInternal && !vpnConnected
            ? TrafficSource.Local
            : vpnConnected ? TrafficSource.Vpn : TrafficSource.None;
        if (source != _trafficSource)
        {
            _trafficSource = source;
            _trafficSamples.Clear();
            SentLine.Stroke = source == TrafficSource.Local
                ? (Brush)Application.Current.Resources["GraphLocalSentBrush"]
                : (Brush)Application.Current.Resources["GraphSentBrush"];
            ReceivedLine.Stroke = source == TrafficSource.Local
                ? (Brush)Application.Current.Resources["GraphLocalReceivedBrush"]
                : (Brush)Application.Current.Resources["GraphReceivedBrush"];
        }

        if (source == TrafficSource.None)
        {
            RenderTrafficGraph();
            return;
        }

        var sent = 0d;
        var received = 0d;
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                && (source == TrafficSource.Vpn ? IsVpnInterface(adapter) : IsLocalTrafficInterface(adapter)))
            .ToArray();
        if (adapters.Length > 0)
        {
            try
            {
                long totalSent = 0;
                long totalReceived = 0;
                foreach (var adapter in adapters)
                {
                    try
                    {
                        var statistics = adapter.GetIPv4Statistics();
                        totalSent += statistics.BytesSent;
                        totalReceived += statistics.BytesReceived;
                    }
                    catch (NetworkInformationException)
                    {
                        var statistics = adapter.GetIPStatistics();
                        totalSent += statistics.BytesSent;
                        totalReceived += statistics.BytesReceived;
                    }
                }
                var now = DateTimeOffset.UtcNow;
                if (_trafficSamples.Count > 0)
                {
                    var previous = _trafficSamples.Last();
                    var seconds = Math.Max((now - previous.Timestamp).TotalSeconds, 0.1);
                    sent = Math.Max(0, (totalSent - previous.TotalSent) / seconds);
                    received = Math.Max(0, (totalReceived - previous.TotalReceived) / seconds);
                }
                _trafficSamples.Enqueue(new TrafficSample(now, totalSent, totalReceived, sent, received));
            }
            catch (NetworkInformationException)
            {
            }
        }

        while (_trafficSamples.Count > 30)
            _trafficSamples.Dequeue();
        RenderTrafficGraph();
    }

    private static bool IsLocalTrafficInterface(NetworkInterface adapter) =>
        adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211;

    private void RenderTrafficGraph()
    {
        DrawGraphGrid();
        var samples = _trafficSamples.ToArray();
        SentLine.Points.Clear();
        ReceivedLine.Points.Clear();
        if (samples.Length == 0 || TrafficCanvas.ActualWidth <= 0)
            return;

        var maximum = Math.Max(1, samples.Max(sample => Math.Max(sample.SentPerSecond, sample.ReceivedPerSecond)));
        var width = TrafficCanvas.ActualWidth;
        var height = TrafficCanvas.ActualHeight;
        for (var index = 0; index < samples.Length; index++)
        {
            var x = samples.Length == 1 ? width : index * width / (samples.Length - 1);
            var sentY = height - (samples[index].SentPerSecond / maximum * (height - 8)) - 4;
            var receivedY = height - (samples[index].ReceivedPerSecond / maximum * (height - 8)) - 4;
            SentLine.Points.Add(new Point(x, sentY));
            ReceivedLine.Points.Add(new Point(x, receivedY));
        }
    }

    private void DrawGraphGrid()
    {
        foreach (var line in TrafficCanvas.Children.OfType<System.Windows.Shapes.Line>().ToArray())
            TrafficCanvas.Children.Remove(line);
        var width = TrafficCanvas.ActualWidth;
        var height = TrafficCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return;
        const double fadeWidth = 30;
        GraphFadeLeft.Width = fadeWidth;
        GraphFadeRight.Width = fadeWidth;
        GraphFadeLeft.Height = height;
        GraphFadeRight.Height = height;
        Canvas.SetLeft(GraphFadeLeft, 0);
        Canvas.SetLeft(GraphFadeRight, Math.Max(0, width - fadeWidth));
        var brush = new SolidColorBrush(ThemeLoader.GetColor("graph-grid", Color.FromRgb(0x3C, 0x3C, 0x3C)));
        for (var column = 1; column < 8; column++)
        {
            var x = column * width / 8;
            TrafficCanvas.Children.Insert(0, new System.Windows.Shapes.Line { X1 = x, X2 = x, Y1 = 0, Y2 = height, Stroke = brush, StrokeThickness = 1 });
        }
        for (var row = 1; row < 6; row++)
        {
            var y = row * height / 6;
            TrafficCanvas.Children.Insert(0, new System.Windows.Shapes.Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = brush, StrokeThickness = 1 });
        }
    }

    private static bool IsVpnInterface(NetworkInterface adapter)
    {
        var text = $"{adapter.Name} {adapter.Description} {adapter.NetworkInterfaceType}";
        return text.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Wintun", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TAP", StringComparison.OrdinalIgnoreCase)
            || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel;
    }

    private static bool HasActiveVpnInterface() => NetworkInterface.GetAllNetworkInterfaces()
        .Any(adapter => adapter.OperationalStatus == OperationalStatus.Up && IsVpnInterface(adapter));

    private void TrafficCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTrafficGraph();

    private readonly record struct TrafficSample(
        DateTimeOffset Timestamp,
        long TotalSent,
        long TotalReceived,
        double SentPerSecond,
        double ReceivedPerSecond);

    private enum TrafficSource
    {
        None,
        Vpn,
        Local
    }

    private readonly record struct CredentialInput(string Username, string Password, bool Save);

    private void SetMessage(string message) => MessageText.Text = message;
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    internal async void ExitApplication()
    {
        try
        {
            await _vpn.DisconnectUserAsync();
        }
        catch (Exception ex)
        {
            SetMessage($"User VPN shutdown failed: {ex.Message}");
        }
        finally
        {
            _allowClose = true;
            Application.Current.Shutdown();
        }
    }
    private static string? EmptyAsNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class ServiceInstaller
{
    public static bool IsInstalled()
    {
        try
        {
            using var service = new ServiceController("AOVPNService");
            _ = service.Status;
            return true;
        }
        catch (Exception)
        {
            return GetStatus() != "not installed";
        }
    }

    public static string GetStatus()
    {
        try
        {
            using var service = new ServiceController("AOVPNService");
            return service.Status switch
            {
                ServiceControllerStatus.Running => "running",
                ServiceControllerStatus.StartPending => "starting",
                ServiceControllerStatus.StopPending => "stopping",
                ServiceControllerStatus.Stopped => "stopped",
                _ => "installed"
            };
        }
        catch (Exception)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "query AOVPNService",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null)
                return "unknown";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return "not installed";
            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                return "running";
            if (output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase))
                return "starting";
            if (output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase))
                return "stopping";
            if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                return "stopped";
            return "installed";
        }
    }

    public static string? FindServiceExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 7 && directory is not null; i++, directory = directory.Parent)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "service", "AOVPN.Service.exe"),
                Path.Combine(directory.FullName, "service", "bin", "Release", "net7.0-windows", "win-x64", "publish", "AOVPN.Service.exe"),
                Path.Combine(directory.FullName, "service", "bin", "Release", "net7.0-windows", "publish", "AOVPN.Service.exe")
            };
            var found = candidates.FirstOrDefault(File.Exists);
            if (found is not null)
                return found;
        }
        return null;
    }

    public static void Install(string executable, string sourceDirectory, string destinationDirectory)
    {
        RunSc($"create AOVPNService binPath= \"\\\"{executable}\\\"\" start= auto obj= LocalSystem");
        RunElevatedCopy(sourceDirectory, destinationDirectory);
        RunSc("start AOVPNService");
    }

    public static void Remove()
    {
        RunSc("stop AOVPNService", ignoreFailure: true);
        RunSc("delete AOVPNService");
    }

    private static void RunSc(string arguments, bool ignoreFailure = false)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the Windows service controller.");
        process.WaitForExit();
        if (!ignoreFailure && process.ExitCode != 0)
            throw new InvalidOperationException($"sc.exe failed with exit code {process.ExitCode}.");
    }

    private static void RunElevatedCopy(string sourceDirectory, string destinationDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "robocopy.exe",
            Arguments = $"\"{sourceDirectory}\" \"{destinationDirectory}\" /E /IS /IT /R:1 /W:1",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the elevated profile copy.");
        process.WaitForExit();
        if (process.ExitCode > 7)
            throw new InvalidOperationException($"Profile copy failed with exit code {process.ExitCode}.");
    }
}
