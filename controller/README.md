# AOVPN Controller

This is the first controller prototype for location-aware OpenVPN operation.
It does not modify the OpenVPN protocol. It imports profiles, checks an internal-only DNS name, and starts or stops the locally built OpenVPN executable.

Internal detection sends IPv4 A-record DNS queries directly through configured DNS servers on operational physical adapters. VPN, tunnel, TAP, Wintun, loopback, and virtual adapters are excluded, preventing the VPN's DNS configuration from making the machine appear internal after connection.

## Quick start

```powershell
dotnet run -- import C:\path\to\office.ovpn
dotnet run -- set openvpn A:\openvpn-core\out\build\win-amd64-release-aovpn\Release\openvpn.exe
dotnet run -- set dns-host vpn-internal.example.com
dotnet run -- set expected-ip 10.0.0.15
dotnet run -- set auto-connect true
dotnet run -- run
```

Use `set auto-connect false` to leave automatic connection disabled. Manual `connect` and `disconnect` remain available.

The imported profile and referenced relative certificate files are stored below `%LOCALAPPDATA%\AOVPN\profiles`. Settings are stored in `%LOCALAPPDATA%\AOVPN\settings.json`.

This prototype terminates OpenVPN by process termination. A later service implementation should use the OpenVPN management interface for graceful disconnects and should protect the management endpoint.
