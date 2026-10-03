using AOVPN.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AOVPN.Service;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .UseWindowsService(options => options.ServiceName = "AOVPN Service")
            .ConfigureServices(services => services.AddHostedService<MonitorWorker>())
            .Build();
        await host.RunAsync();
    }
}

internal sealed class MonitorWorker : BackgroundService
{
    private readonly SemaphoreSlim _vpnLock = new(1, 1);
    private volatile bool _handedOff;
    private volatile bool _userLoggedOn;
    private OpenVpnController? _vpn;
    private OpenVpnController? _postVpn;
    private AppSettings? _settings;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var paths = AppPaths.Create(machineWide: true);
        _settings = await SettingsStore.LoadAsync(paths);
        _vpn = new OpenVpnController(paths, _settings, _settings.PreLoginProfileId);
        _postVpn = new OpenVpnController(paths, _settings, _settings.PostLoginProfileId ?? _settings.ProfileId);
        _userLoggedOn = Process.GetProcessesByName("explorer").Any(process => process.SessionId != 0);
        SystemEvents.SessionSwitch += OnSessionSwitch;
        var pipeTask = RunHandoffPipeAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var internalNetwork = await NetworkDetector.IsInternalAsync(_settings, stoppingToken);
                    if (internalNetwork)
                    {
                        _handedOff = false;
                        await DisconnectAllAsync();
                    }
                    else if (!_userLoggedOn && !_handedOff && _settings.AutoConnect && _settings.PreLoginProfileId is not null && !_vpn.IsRunning)
                        await ConnectAsync();
                }
                catch
                {
                    // Continue monitoring after transient network or VPN errors.
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_settings.DetectionIntervalSeconds, 5, 3600)), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            await DisconnectAllAsync();
            try { await pipeTask; } catch (OperationCanceledException) { }
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs args)
    {
        if (args.Reason == SessionSwitchReason.SessionLogoff)
        {
            _handedOff = false;
            _userLoggedOn = false;
        }
        else if (args.Reason is SessionSwitchReason.SessionLogon or SessionSwitchReason.SessionUnlock)
        {
            _userLoggedOn = true;
            _handedOff = true;
            _ = DisconnectAsync();
        }
    }

    private async Task RunHandoffPipeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var security = new PipeSecurity();
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));
                using var pipe = NamedPipeServerStreamAcl.Create("AOVPN.Handoff", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
            await pipe.WaitForConnectionAsync(cancellationToken);
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            var command = await reader.ReadLineAsync(cancellationToken);
            if (string.Equals(command, "handoff", StringComparison.OrdinalIgnoreCase))
            {
                _handedOff = true;
                await DisconnectAsync();
                await writer.WriteLineAsync("OK");
            }
            else if (string.Equals(command, "connect-post", StringComparison.OrdinalIgnoreCase))
            {
                var username = await reader.ReadLineAsync(cancellationToken);
                var password = await reader.ReadLineAsync(cancellationToken);
                var connected = await ConnectPostAsync(username, password);
                await writer.WriteLineAsync(connected ? "OK" : "ERROR");
            }
            else if (string.Equals(command, "disconnect-post", StringComparison.OrdinalIgnoreCase))
            {
                await DisconnectPostAsync();
                _handedOff = false;
                await writer.WriteLineAsync("OK");
            }
            else if (string.Equals(command, "disconnect-all", StringComparison.OrdinalIgnoreCase))
            {
                _handedOff = true;
                await DisconnectAllAsync();
                await writer.WriteLineAsync("OK");
            }
            else if (string.Equals(command, "rollback", StringComparison.OrdinalIgnoreCase))
            {
                _handedOff = false;
                await writer.WriteLineAsync("OK");
            }
            else if (string.Equals(command, "status", StringComparison.OrdinalIgnoreCase))
            {
                var status = _postVpn?.IsConnected == true ? "POST_CONNECTED"
                    : _postVpn?.IsRunning == true ? "POST_CONNECTING"
                    : _vpn?.IsConnected == true ? "PRE_CONNECTED"
                    : _vpn?.IsRunning == true ? "PRE_CONNECTING"
                    : "DISCONNECTED";
                await writer.WriteLineAsync(status);
            }
            else
            {
                await writer.WriteLineAsync("ERROR");
            }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // Continue listening if a client closes the pipe early.
            }
        }
    }

    private async Task ConnectAsync()
    {
        await _vpnLock.WaitAsync();
        try { await _vpn!.ConnectAsync(); }
        finally { _vpnLock.Release(); }
    }

    private async Task DisconnectAsync()
    {
        await _vpnLock.WaitAsync();
        try { await _vpn!.DisconnectAsync(); }
        finally { _vpnLock.Release(); }
    }

    private async Task<bool> ConnectPostAsync(string? username, string? password)
    {
        _handedOff = true;
        await DisconnectAsync();
        await _vpnLock.WaitAsync();
        try
        {
            if (_postVpn is null)
                return false;
            await _postVpn.ConnectAsync(username, password);
            if (await _postVpn.WaitForConnectedAsync(TimeSpan.FromSeconds(30)))
                return true;
            await _postVpn.DisconnectAsync();
            _handedOff = false;
            return false;
        }
        finally { _vpnLock.Release(); }
    }

    private async Task DisconnectPostAsync()
    {
        await _vpnLock.WaitAsync();
        try { if (_postVpn is not null) await _postVpn.DisconnectAsync(); }
        finally { _vpnLock.Release(); }
    }

    private async Task DisconnectAllAsync()
    {
        await DisconnectPostAsync();
        await DisconnectAsync();
    }
}
