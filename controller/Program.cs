using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.IO.Pipes;
using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace AOVPN.Controller;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var paths = AppPaths.Create();
        var settings = await SettingsStore.LoadAsync(paths);
        var vpn = new OpenVpnController(paths, settings);

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "import" => await ImportAsync(args, paths, settings),
                "settings" => await PrintSettingsAsync(settings),
                "set" => await SetAsync(args, paths, settings),
                "status" => await PrintStatusAsync(paths, settings, vpn),
                "connect" => await vpn.ConnectAsync(),
                "disconnect" => await vpn.DisconnectAsync(),
                "run" => await RunAsync(paths, settings, vpn),
                _ => UsageError()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AOVPN error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ImportAsync(string[] args, AppPaths paths, AppSettings settings)
    {
        if (args.Length != 2)
            return UsageError("import requires a .ovpn file path.");

        var profile = await ProfileImporter.ImportAsync(args[1], paths);
        settings.ProfileId = profile.Id;
        await SettingsStore.SaveAsync(paths, settings);
        Console.WriteLine($"Imported '{profile.Name}'.");
        Console.WriteLine($"Profile: {profile.ConfigPath}");
        return 0;
    }

    private static async Task<int> SetAsync(string[] args, AppPaths paths, AppSettings settings)
    {
        if (args.Length != 3)
            return UsageError("set requires a setting name and value.");

        switch (args[1].ToLowerInvariant())
        {
            case "dns-host":
                settings.InternalDnsHost = args[2];
                break;
            case "expected-ip":
                settings.ExpectedInternalIp = IPAddress.Parse(args[2]).ToString();
                break;
            case "auto-connect":
                settings.AutoConnect = ParseBoolean(args[2]);
                break;
            case "interval":
                settings.DetectionIntervalSeconds = Math.Clamp(int.Parse(args[2]), 5, 3600);
                break;
            case "openvpn":
                settings.OpenVpnExecutable = Path.GetFullPath(args[2]);
                break;
            default:
                return UsageError($"Unknown setting '{args[1]}'.");
        }

        await SettingsStore.SaveAsync(paths, settings);
        Console.WriteLine("Settings saved.");
        return 0;
    }

    private static async Task<int> RunAsync(AppPaths paths, AppSettings settings, OpenVpnController vpn)
    {
        if (string.IsNullOrWhiteSpace(settings.InternalDnsHost))
            return UsageError("Set dns-host before running the monitor.");

        Console.WriteLine("AOVPN monitor running. Press Ctrl+C to stop.");
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        while (!cancellation.IsCancellationRequested)
        {
            var internalNetwork = await NetworkDetector.IsInternalAsync(settings, cancellation.Token);
            Console.WriteLine($"[{DateTimeOffset.Now:u}] Network: {(internalNetwork ? "internal" : "external")}");

            if (internalNetwork)
                await vpn.DisconnectAsync();
            else if (settings.AutoConnect)
                await vpn.ConnectAsync();

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.DetectionIntervalSeconds), cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await vpn.DisconnectAsync();
        return 0;
    }

    private static async Task<int> PrintSettingsAsync(AppSettings settings)
    {
        Console.WriteLine(JsonSerializer.Serialize(settings, JsonDefaults.Options));
        await Task.CompletedTask;
        return 0;
    }

    private static async Task<int> PrintStatusAsync(AppPaths paths, AppSettings settings, OpenVpnController vpn)
    {
        var internalNetwork = string.IsNullOrWhiteSpace(settings.InternalDnsHost)
            ? false
            : await NetworkDetector.IsInternalAsync(settings, CancellationToken.None);
        Console.WriteLine($"Internal network: {internalNetwork}");
        Console.WriteLine($"Auto-connect: {settings.AutoConnect}");
        Console.WriteLine($"VPN running: {vpn.IsRunning}");
        Console.WriteLine($"Profile: {settings.ProfileId ?? "not configured"}");
        return 0;
    }

    private static bool ParseBoolean(string value) => value.ToLowerInvariant() switch
    {
        "true" or "on" or "yes" or "1" => true,
        "false" or "off" or "no" or "0" => false,
        _ => throw new ArgumentException("Boolean values must be true or false.")
    };

    private static int UsageError(string? message = null)
    {
        if (message is not null)
            Console.Error.WriteLine(message);
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("AOVPN controller");
        Console.WriteLine("  import <file.ovpn>       Import an OpenVPN profile");
        Console.WriteLine("  set dns-host <hostname>  Internal-only DNS name");
        Console.WriteLine("  set expected-ip <ip>     Optional DNS result validation");
        Console.WriteLine("  set auto-connect <bool>  Enable or disable automatic VPN connection");
        Console.WriteLine("  set interval <seconds>   DNS detection interval");
        Console.WriteLine("  set openvpn <path>       OpenVPN executable path");
        Console.WriteLine("  settings                 Show saved settings");
        Console.WriteLine("  status                   Show detection and VPN status");
        Console.WriteLine("  connect | disconnect     Control the VPN manually");
        Console.WriteLine("  run                      Run the location-aware monitor");
    }
}

public sealed class AppPaths
{
    public required string Root { get; init; }
    public required string Profiles { get; init; }
    public required string Settings { get; init; }

    public static AppPaths Create(bool machineWide = false)
    {
        var basePath = machineWide
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(basePath, "AOVPN");
        Directory.CreateDirectory(root);
        var profiles = Path.Combine(root, "profiles");
        Directory.CreateDirectory(profiles);
        return new AppPaths { Root = root, Profiles = profiles, Settings = Path.Combine(root, "settings.json") };
    }
}

public sealed class AppSettings
{
    public bool AutoConnect { get; set; } = true;
    public string? InternalDnsHost { get; set; }
    public string? ExpectedInternalIp { get; set; }
    public int DetectionIntervalSeconds { get; set; } = 30;
    public string? ProfileId { get; set; }
    public string? ProfileName { get; set; }
    public string? PreLoginProfileId { get; set; }
    public string? PreLoginProfileName { get; set; }
    public string? PostLoginProfileId { get; set; }
    public string? PostLoginProfileName { get; set; }
    public string OpenVpnExecutable { get; set; } = "openvpn.exe";
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public static class SettingsStore
{
    public static async Task<AppSettings> LoadAsync(AppPaths paths)
    {
        if (!File.Exists(paths.Settings))
            return new AppSettings();
        await using var stream = File.OpenRead(paths.Settings);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonDefaults.Options).ConfigureAwait(false) ?? new AppSettings();
        if (settings.PostLoginProfileId is null && settings.ProfileId is not null)
        {
            settings.PostLoginProfileId = settings.ProfileId;
            settings.PostLoginProfileName = settings.ProfileName;
        }
        return settings;
    }

    public static async Task SaveAsync(AppPaths paths, AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        await File.WriteAllTextAsync(paths.Settings, json);
        var temporary = paths.Settings + ".tmp";
        if (File.Exists(temporary))
            File.Delete(temporary);
    }
}

public sealed class ImportedProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ConfigPath { get; init; }
}

public static class ProfileImporter
{
    private static readonly string[] ReferencedDirectives =
    {
        "ca", "cert", "key", "pkcs12", "tls-auth", "tls-crypt", "tls-crypt-v2", "crl-verify", "auth-user-pass"
    };

    public static async Task<ImportedProfile> ImportAsync(string inputPath, AppPaths paths, string? profileName = null)
    {
        var source = Path.GetFullPath(inputPath);
        if (!File.Exists(source) || !source.EndsWith(".ovpn", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The supplied file must be an existing .ovpn file.", source);

        var id = Guid.NewGuid().ToString("N");
        var destinationDirectory = Path.Combine(paths.Profiles, id);
        Directory.CreateDirectory(destinationDirectory);
        var destinationConfig = Path.Combine(destinationDirectory, Path.GetFileName(source));
        File.Copy(source, destinationConfig);

        foreach (var referencedFile in FindReferencedFiles(await File.ReadAllLinesAsync(source), Path.GetDirectoryName(source)!))
        {
            var destinationFile = Path.Combine(destinationDirectory, referencedFile.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(referencedFile.FullPath, destinationFile, true);
        }

        return new ImportedProfile
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(profileName) ? Path.GetFileNameWithoutExtension(source) : profileName.Trim(),
            ConfigPath = destinationConfig
        };
    }

    private static IEnumerable<(string FullPath, string RelativePath)> FindReferencedFiles(IEnumerable<string> lines, string sourceDirectory)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                continue;
            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !ReferencedDirectives.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                continue;
            var value = parts[1].Trim('"');
            if (value.StartsWith("<") || Path.IsPathRooted(value))
                continue;
            var candidate = Path.GetFullPath(Path.Combine(sourceDirectory, value));
            var sourceRoot = Path.GetFullPath(sourceDirectory + Path.DirectorySeparatorChar);
            if (candidate.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                yield return (candidate, value);
        }
    }
}

public static class NetworkDetector
{
    public static async Task<bool> IsInternalAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.InternalDnsHost))
            return false;

        try
        {
            var addresses = await ResolveUsingPhysicalDnsAsync(settings.InternalDnsHost, cancellationToken);
            if (addresses.Length == 0)
                return false;
            return string.IsNullOrWhiteSpace(settings.ExpectedInternalIp)
                || addresses.Any(address => address.ToString() == settings.ExpectedInternalIp);
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<IPAddress[]> ResolveUsingPhysicalDnsAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
            return new[] { literal };

        var queries = new List<Task<IPAddress[]>>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || IsVirtualAdapter(adapter))
                continue;

            var properties = adapter.GetIPProperties();
            var localAddresses = properties.UnicastAddresses.Select(item => item.Address).ToArray();
            foreach (var dnsServer in properties.DnsAddresses)
            {
                var localAddress = localAddresses.FirstOrDefault(address => address.AddressFamily == dnsServer.AddressFamily);
                if (localAddress is null)
                    continue;

                queries.Add(QueryDnsSafeAsync(host, localAddress, dnsServer, cancellationToken));
            }
        }
        var responses = await Task.WhenAll(queries);
        return responses.SelectMany(addresses => addresses).Distinct().ToArray();
    }

    private static async Task<IPAddress[]> QueryDnsSafeAsync(string host, IPAddress localAddress, IPAddress dnsServer, CancellationToken cancellationToken)
    {
        try
        {
            return await QueryDnsAsync(host, 1, localAddress, dnsServer, cancellationToken);
        }
        catch (SocketException) { return Array.Empty<IPAddress>(); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Array.Empty<IPAddress>(); }
    }

    private static async Task<IPAddress[]> QueryDnsAsync(
        string host,
        ushort queryType,
        IPAddress localAddress,
        IPAddress dnsServer,
        CancellationToken cancellationToken)
    {
        using var client = new UdpClient(localAddress.AddressFamily);
        client.Client.Bind(new IPEndPoint(localAddress, 0));
        client.Connect(new IPEndPoint(dnsServer, 53));

        var id = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        var query = BuildDnsQuery(host, queryType, id);
        await client.SendAsync(query, query.Length);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        var response = await client.ReceiveAsync(timeout.Token);
        return ParseDnsResponse(response.Buffer, id, queryType);
    }

    private static byte[] BuildDnsQuery(string host, ushort queryType, ushort id)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(IPAddress.HostToNetworkOrder((short)id));
        writer.Write(IPAddress.HostToNetworkOrder((short)0x0100));
        writer.Write(IPAddress.HostToNetworkOrder((short)1));
        writer.Write(IPAddress.HostToNetworkOrder((short)0));
        writer.Write(IPAddress.HostToNetworkOrder((short)0));
        writer.Write(IPAddress.HostToNetworkOrder((short)0));
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
        writer.Write((byte)0);
        writer.Write(IPAddress.HostToNetworkOrder((short)queryType));
        writer.Write(IPAddress.HostToNetworkOrder((short)1));
        return stream.ToArray();
    }

    private static IPAddress[] ParseDnsResponse(byte[] response, ushort id, ushort queryType)
    {
        if (response.Length < 12 || ReadUInt16(response, 0) != id)
            return Array.Empty<IPAddress>();

        var answerCount = ReadUInt16(response, 6);
        var offset = 12;
        offset = SkipDnsName(response, offset);
        offset += 4;
        var addresses = new List<IPAddress>();
        for (var index = 0; index < answerCount && offset + 12 <= response.Length; index++)
        {
            offset = SkipDnsName(response, offset);
            if (offset + 10 > response.Length)
                break;
            var type = ReadUInt16(response, offset);
            var classCode = ReadUInt16(response, offset + 2);
            var dataLength = ReadUInt16(response, offset + 8);
            offset += 10;
            if (offset + dataLength > response.Length)
                break;
            if (classCode == 1 && type == queryType && ((queryType == 1 && dataLength == 4) || (queryType == 28 && dataLength == 16)))
                addresses.Add(new IPAddress(response.AsSpan(offset, dataLength)));
            offset += dataLength;
        }
        return addresses.ToArray();
    }

    private static int SkipDnsName(byte[] data, int offset)
    {
        while (offset < data.Length)
        {
            var length = data[offset++];
            if (length == 0)
                break;
            if ((length & 0xC0) == 0xC0)
            {
                offset++;
                break;
            }
            offset += length;
        }
        return offset;
    }

    private static ushort ReadUInt16(byte[] data, int offset) => (ushort)((data[offset] << 8) | data[offset + 1]);

    private static bool IsVirtualAdapter(NetworkInterface adapter)
    {
        var text = $"{adapter.Name} {adapter.Description} {adapter.NetworkInterfaceType}";
        return adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
            || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel
            || text.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Wintun", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TAP", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class OpenVpnController
{
    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private Process? _process;
    private string? _credentialFile;
    private string? _logPath;
    private string? _lastLogPath;
    private volatile bool _connected;
    private readonly SemaphoreSlim _logLock = new(1, 1);

    private readonly string? _profileId;

    public OpenVpnController(AppPaths paths, AppSettings settings, string? profileId = null)
    {
        _paths = paths;
        _settings = settings;
        _profileId = profileId;
    }

    public bool IsRunning => _process is { HasExited: false };
    public bool IsConnected => _connected;

    public string? CurrentConfigPath => GetCurrentConfigPath();
    public string? CurrentLogPath => _logPath ?? _lastLogPath ?? GetLatestLogPath();

    public Task<int> ConnectAsync() => ConnectAsync(null, null);

    public Task<int> DisconnectUserAsync() => DisconnectAsync(userOnly: true);

    public async Task<int> ConnectAsync(string? username, string? password)
    {
        if (IsRunning)
            return 0;
        if (string.IsNullOrWhiteSpace(_profileId ?? _settings.ProfileId))
            throw new InvalidOperationException("Import an .ovpn profile first.");

        var config = GetCurrentConfigPath();
        if (config is null)
            throw new FileNotFoundException("The configured imported profile is missing.");
        var profileDirectory = Path.GetDirectoryName(config)!;
        _logPath = Path.Combine(profileDirectory, $"connection-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _lastLogPath = _logPath;
        await File.WriteAllTextAsync(_logPath, $"AOVPN OpenVPN session started at {DateTimeOffset.Now:u}{Environment.NewLine}");
        _connected = false;

        var executable = OpenVpnLocator.Find(_settings.OpenVpnExecutable);
        if (executable is null)
            throw new FileNotFoundException("OpenVPN was not found. Use the OpenVPN executable Browse button and select openvpn.exe.");

        StageRuntimeLibraries(executable);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = profileDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        AddRuntimeSearchPaths(startInfo, executable);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(config);
        startInfo.ArgumentList.Add("--windows-driver");
        startInfo.ArgumentList.Add("dco");
        if (!string.IsNullOrWhiteSpace(username))
        {
            _credentialFile = Path.Combine(profileDirectory, "credentials.tmp");
            await File.WriteAllTextAsync(_credentialFile, $"{username}{Environment.NewLine}{password ?? string.Empty}{Environment.NewLine}");
            startInfo.ArgumentList.Add("--auth-user-pass");
            startInfo.ArgumentList.Add(_credentialFile);
        }
        startInfo.ArgumentList.Add("--verb");
        startInfo.ArgumentList.Add("3");
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start OpenVPN.");
        _ = CaptureOutputAsync(_process.StandardOutput, _logPath);
        _ = CaptureOutputAsync(_process.StandardError, _logPath);
        Console.WriteLine($"OpenVPN started (PID {_process.Id}).");
        await Task.CompletedTask;
        return 0;
    }

    private static void AddRuntimeSearchPaths(ProcessStartInfo startInfo, string executable)
    {
        var directories = new List<string> { Path.GetDirectoryName(executable)! };
        var directory = new DirectoryInfo(Path.GetDirectoryName(executable)!);
        for (var i = 0; i < 7 && directory is not null; i++, directory = directory.Parent)
        {
            directories.Add(Path.Combine(directory.FullName, "vcpkg", "installed", "x64-windows-ovpn", "bin"));
            directories.Add(Path.Combine(directory.FullName, "vcpkg", "installed", "x64-windows-ovpn", "debug", "bin"));
        }

        var existing = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        startInfo.Environment["PATH"] = string.Join(Path.PathSeparator, directories
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Append(existing));
    }

    private static void StageRuntimeLibraries(string executable)
    {
        var executableDirectory = Path.GetDirectoryName(executable)!;
        var directory = new DirectoryInfo(executableDirectory);
        for (var i = 0; i < 7 && directory is not null; i++, directory = directory.Parent)
        {
            var runtimeDirectory = Path.Combine(directory.FullName, "vcpkg", "installed", "x64-windows-ovpn", "bin");
            if (!Directory.Exists(runtimeDirectory))
                continue;

            foreach (var library in Directory.EnumerateFiles(runtimeDirectory, "*.dll"))
            {
                var destination = Path.Combine(executableDirectory, Path.GetFileName(library));
                if (!File.Exists(destination))
                    File.Copy(library, destination);
            }
            return;
        }
    }

    public async Task<int> DisconnectAsync()
        => await DisconnectAsync(userOnly: false);

    private async Task<int> DisconnectAsync(bool userOnly)
    {
        if (!IsRunning)
        {
            StopOrphanedProcess(userOnly);
            return 0;
        }
        _process!.Kill(true);
        await _process.WaitForExitAsync();
        _process.Dispose();
        _process = null;
        _connected = false;
        _logPath = null;
        if (_credentialFile is not null)
        {
            File.Delete(_credentialFile);
            _credentialFile = null;
        }
        Console.WriteLine("OpenVPN stopped.");
        return 0;
    }

    private void StopOrphanedProcess(bool userOnly)
    {
        var executable = OpenVpnLocator.Find(_settings.OpenVpnExecutable);
        if (executable is null)
            return;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            try
            {
                if (userOnly && process.SessionId == 0)
                    continue;
                if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(true);
                    process.WaitForExit(3000);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
    }

    private string? GetCurrentConfigPath()
    {
        if (string.IsNullOrWhiteSpace(_profileId ?? _settings.ProfileId))
            return null;
        var profileDirectory = Path.Combine(_paths.Profiles, _profileId ?? _settings.ProfileId!);
        return Directory.Exists(profileDirectory)
            ? Directory.EnumerateFiles(profileDirectory, "*.ovpn").SingleOrDefault()
            : null;
    }

    private string? GetLatestLogPath()
    {
        var config = GetCurrentConfigPath();
        if (config is null)
            return null;
        var directory = Path.GetDirectoryName(config)!;
        return Directory.EnumerateFiles(directory, "connection-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private async Task CaptureOutputAsync(StreamReader reader, string logPath)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Contains("Initialization Sequence Completed", StringComparison.OrdinalIgnoreCase))
                    _connected = true;
                await _logLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await File.AppendAllTextAsync(logPath, line + Environment.NewLine).ConfigureAwait(false);
                }
                finally
                {
                    _logLock.Release();
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    public async Task<bool> WaitForConnectedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (_connected)
                return true;
            if (!IsRunning)
                return false;
            await Task.Delay(250, cancellationToken);
        }
        return _connected;
    }
}

public static class OpenVpnLocator
{
    public static string? Find(string? configuredPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
            candidates.Add(configuredPath);

        if (string.Equals(configuredPath, "openvpn.exe", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenVPN", "bin", "openvpn.exe"));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "openvpn.exe"));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "openvpn", "openvpn.exe"));

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 7 && directory is not null; i++, directory = directory.Parent)
            {
                candidates.Add(Path.Combine(directory.FullName, "openvpn-core", "out", "build", "win-amd64-release-aovpn", "Release", "openvpn.exe"));
            }

            var path = Environment.GetEnvironmentVariable("PATH");
            if (path is not null)
            {
                candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(directory => Path.Combine(directory, "openvpn.exe")));
            }
        }

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }
}

public static class ServiceHandoff
{
    public static async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", "AOVPN.Handoff", PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1000, cancellationToken);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            using var reader = new StreamReader(pipe);
            await writer.WriteLineAsync("status");
            return await reader.ReadLineAsync(cancellationToken) ?? "DISCONNECTED";
        }
         catch (IOException) { return "UNAVAILABLE"; }
         catch (TimeoutException) { return "UNAVAILABLE"; }
         catch (ObjectDisposedException) { return "UNAVAILABLE"; }
         catch (InvalidOperationException) { return "UNAVAILABLE"; }
    }

    public static async Task<bool> ConnectPostAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", "AOVPN.Handoff", PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1000, cancellationToken);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            using var reader = new StreamReader(pipe);
            await writer.WriteLineAsync("connect-post");
            await writer.WriteLineAsync(username);
            await writer.WriteLineAsync(password);
            return string.Equals(await reader.ReadLineAsync(cancellationToken), "OK", StringComparison.OrdinalIgnoreCase);
        }
         catch (IOException) { return false; }
         catch (TimeoutException) { return false; }
         catch (ObjectDisposedException) { return false; }
         catch (InvalidOperationException) { return false; }
    }

    public static async Task<bool> SendAsync(string command, CancellationToken cancellationToken = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", "AOVPN.Handoff", PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1000, cancellationToken);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            using var reader = new StreamReader(pipe);
            await writer.WriteLineAsync(command);
            return string.Equals(await reader.ReadLineAsync(cancellationToken), "OK", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
         catch (TimeoutException)
         {
             return false;
         }
         catch (ObjectDisposedException)
         {
             return false;
         }
         catch (InvalidOperationException)
         {
             return false;
         }
    }
}
