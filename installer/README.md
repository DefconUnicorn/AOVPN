# AOVPN MSI Installer

The installer is per-machine and requires administrator privileges. It installs
the desktop client, AOVPN service, OpenVPN runtime, assets, and license notices
under `%ProgramFiles%\AOVPN`.

The MSI registers `AOVPNService` as an automatic `LocalSystem` service and
stops/removes it during uninstall. The MSI does not install the Windows DCO
driver; install a matching signed DCO driver from the OpenVPN distribution
before starting a VPN connection.

## Build

For a self-contained release, stage the OpenVPN runtime under
`test-package\AOVPN\openvpn`, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build-release.ps1
```

The script publishes the desktop and service with the `win-x64` runtime and
builds the MSI from `release-package\AOVPN`.

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
