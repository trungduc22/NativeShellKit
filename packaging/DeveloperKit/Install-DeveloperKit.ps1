[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\NativeShellKit\DeveloperKit"),
    [string]$PayloadDir = "",
    [switch]$Upgrade,
    [switch]$SkipDotNetRegistration
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$setupExe = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) "artifacts\developer-kit\NativeShellKit-DeveloperKit-Setup-x64.exe"
if (-not (Test-Path $setupExe)) {
    throw "Setup executable not found at '$setupExe'. Run packaging\Build-DeveloperKit.ps1 first."
}

$args = @()
$args += if ($Upgrade) { "--upgrade" } else { "--install" }
$args += @("--install-dir", $InstallDir)
if (-not [string]::IsNullOrWhiteSpace($PayloadDir)) {
    $args += @("--payload-dir", $PayloadDir)
}
if ($SkipDotNetRegistration) {
    $args += "--skip-dotnet-registration"
}

& $setupExe @args
if ($LASTEXITCODE -ne 0) { throw "Developer Kit installer failed with exit code $LASTEXITCODE" }
