param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $root "release-package\AOVPN"
$existingPackage = Join-Path $root "test-package\AOVPN"

if (-not (Test-Path -LiteralPath (Join-Path $root "desktop"))) {
    throw "The desktop project directory was not found."
}
if (-not (Test-Path -LiteralPath (Join-Path $root "service"))) {
    throw "The service project directory was not found."
}
if (-not (Test-Path -LiteralPath (Join-Path $existingPackage "openvpn\openvpn.exe"))) {
    throw "Stage the OpenVPN runtime under test-package\AOVPN\openvpn before building the release."
}

if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging | Out-Null

dotnet publish (Join-Path $root "desktop\AOVPN.Desktop.csproj") -c $Configuration -r $Runtime --self-contained true -o $staging
dotnet publish (Join-Path $root "service\AOVPN.Service.csproj") -c $Configuration -r $Runtime --self-contained true -o (Join-Path $staging "service")
Copy-Item -LiteralPath (Join-Path $existingPackage "openvpn") -Destination $staging -Recurse
Copy-Item -LiteralPath (Join-Path $existingPackage "README.txt") -Destination $staging -Force

dotnet build (Join-Path $PSScriptRoot "AOVPN.Installer.wixproj") -c $Configuration -p:PackageDir=$staging
Write-Output (Join-Path $PSScriptRoot "bin\$Configuration\AOVPN-Setup.msi")
