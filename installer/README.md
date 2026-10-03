# AOVPN MSI Installer

The installer is per-machine and requires administrator privileges. It installs
the desktop client, AOVPN service, OpenVPN runtime, assets, and license notices
under `%ProgramFiles%\AOVPN`.

The MSI registers `AOVPNService` as an automatic `LocalSystem` service and
stops/removes it during uninstall. The MSI includes the official signed
OpenVPN DCO x64 merge module version 2.7.1 and installs/removes the driver with
the MSI. Its MIT license notice is included in the package license folder.

## Build

For a self-contained release, stage the OpenVPN runtime under
`test-package\AOVPN\openvpn`, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build-release.ps1
```

The script publishes the desktop and service with the `win-x64` runtime and
builds the MSI from `release-package\AOVPN`.

VPN profiles and `settings.json` are intentionally rejected from release
staging. Users import their pre-login and post-login `.ovpn` profiles after
installation; runtime profiles and settings are stored under `%ProgramData%\AOVPN`.

Interactive installs launch the desktop tray application after installation
and register it under the machine-wide Windows logon startup entries. Silent
and Group Policy installs do not launch an interactive GUI; the GUI starts at
the next user logon.

For a framework-dependent test package, first publish the desktop and service
projects into `test-package\AOVPN`, then build directly:

```powershell
dotnet build .\installer\AOVPN.Installer.wixproj -c Release
```

The MSI is written to `installer\bin\Release\AOVPN-Setup.msi`.

## Deployment

Interactive installation:

```powershell
msiexec /i AOVPN-Setup.msi
```

Silent per-machine installation suitable for software deployment or Group
Policy:

```powershell
msiexec /i AOVPN-Setup.msi /qn /norestart
```

Uninstall:

```powershell
msiexec /x AOVPN-Setup.msi /qn /norestart
```
