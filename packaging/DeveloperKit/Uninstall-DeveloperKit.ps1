[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\NativeShellKit\DeveloperKit"),
    [switch]$SkipDotNetRegistration
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$setupExe = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) "artifacts\developer-kit\NativeShellKit-DeveloperKit-Setup-x64.exe"
if (-not (Test-Path $setupExe)) {
    throw "Setup executable not found at '$setupExe'."
}

$args = @("--uninstall", "--install-dir", $InstallDir)
if ($SkipDotNetRegistration) {
    $args += "--skip-dotnet-registration"
}

& $setupExe @args
if ($LASTEXITCODE -ne 0) { throw "Developer Kit uninstaller failed with exit code $LASTEXITCODE" }
