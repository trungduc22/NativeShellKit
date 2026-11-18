[CmdletBinding()]
param(
    [string]$Version = "0.1.0-alpha.1",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$AdditionalNuGetSource = "",
    [switch]$SkipSelfTest
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$devKitRoot = Join-Path $repoRoot "artifacts\developer-kit"
$payloadDir = Join-Path $devKitRoot "payload"

if (Test-Path $devKitRoot) { Remove-Item $devKitRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null

Write-Host "[1/5] Ensuring NuGet packages exist..."
$packagesOut = Join-Path $repoRoot "artifacts\packages"
if (-not (Test-Path (Join-Path $packagesOut "feed-manifest.json"))) {
    $packScript = Join-Path $repoRoot "packaging\Pack-NuGet.ps1"
    $packArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $packScript, "-Version", $Version, "-Configuration", $Configuration, "-Platform", $Platform)
    if (-not [string]::IsNullOrWhiteSpace($AdditionalNuGetSource)) {
        $packArgs += @("-AdditionalNuGetSource", $AdditionalNuGetSource)
    }
    & powershell @packArgs
    if ($LASTEXITCODE -ne 0) { throw "Pack-NuGet.ps1 failed ($LASTEXITCODE)." }
}

Write-Host "[2/5] Staging Developer Kit payload..."
$pkgDest = Join-Path $payloadDir "packages"
New-Item -ItemType Directory -Force -Path $pkgDest | Out-Null
Copy-Item -Path (Join-Path $packagesOut "NativeShellKit.*.nupkg") -Destination $pkgDest -Force
Copy-Item -Path (Join-Path $packagesOut "SHA256SUMS.txt") -Destination $pkgDest -Force
Copy-Item -Path (Join-Path $packagesOut "feed-manifest.json") -Destination $pkgDest -Force

$tplDest = Join-Path $payloadDir "templates"
New-Item -ItemType Directory -Force -Path $tplDest | Out-Null
Copy-Item -Path (Join-Path $packagesOut "NativeShellKit.Templates.*.nupkg") -Destination $tplDest -Force
Copy-Item -Path (Join-Path $repoRoot "templates\NativeShellKit.Templates\content\NativeShellKit.App") -Destination (Join-Path $tplDest "NativeShellKit.App") -Recurse -Force

$samplesDest = Join-Path $payloadDir "samples"
New-Item -ItemType Directory -Force -Path $samplesDest | Out-Null
Copy-Item -Path (Join-Path $repoRoot "samples\Directory.Build.props") -Destination $samplesDest -Force
foreach ($sampleName in @("MinimalNativeApp", "BackgroundCompanionApp", "UnifiedHostDemo")) {
    $srcSample = Join-Path $repoRoot "samples\$sampleName"
    $dstSample = Join-Path $samplesDest $sampleName
    Get-ChildItem -Path $srcSample -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\(bin|obj|\.vs)\\'
    } | ForEach-Object {
        $rel = $_.FullName.Substring($srcSample.Length).TrimStart('\')
        $target = Join-Path $dstSample $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -Path $_.FullName -Destination $target -Force
    }
}

$docsDest = Join-Path $payloadDir "docs"
New-Item -ItemType Directory -Force -Path $docsDest | Out-Null
Copy-Item -Path (Join-Path $repoRoot "README.md") -Destination $docsDest -Force
Copy-Item -Path (Join-Path $repoRoot "AGENTS.md") -Destination $docsDest -Force
Copy-Item -Path (Join-Path $repoRoot "LICENSE") -Destination $docsDest -Force
Copy-Item -Path (Join-Path $repoRoot "docs\*") -Destination $docsDest -Recurse -Force

Write-Host "[3/5] Building nsk CLI and Developer Kit Setup executable..."
$nskDest = Join-Path $payloadDir "tools\nsk"
& dotnet build (Join-Path $repoRoot "tools\NativeShellKit.DevTools\NativeShellKit.DevTools.csproj") -c $Configuration -p:Platform=$Platform -p:Version=$Version --no-restore -o $nskDest
if ($LASTEXITCODE -ne 0) { throw "DevTools build failed ($LASTEXITCODE)." }

$installerProj = Join-Path $repoRoot "tools\NativeShellKit.Installer\NativeShellKit.Installer.csproj"
if ([string]::IsNullOrWhiteSpace($AdditionalNuGetSource)) {
    & dotnet restore $installerProj
} else {
    & dotnet restore $installerProj "-p:RestoreAdditionalProjectSources=$AdditionalNuGetSource"
}
if ($LASTEXITCODE -ne 0) { throw "Installer restore failed ($LASTEXITCODE)." }

& dotnet build $installerProj -c $Configuration -p:Platform=$Platform -p:Version=$Version --no-restore -o $devKitRoot
if ($LASTEXITCODE -ne 0) { throw "Installer build failed ($LASTEXITCODE)." }

$setupExe = Join-Path $devKitRoot "NativeShellKit-DeveloperKit-Setup-x64.exe"
if (-not (Test-Path $setupExe)) { throw "Expected setup exe missing: $setupExe" }

Write-Host "[4/5] Checking optional Inno Setup compiler (ISCC.exe)..."
$iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $foundPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($foundPath) { $iscc = Get-Item $foundPath }
}
if ($iscc) {
    & $iscc.FullName (Join-Path $repoRoot "packaging\DeveloperKit\DeveloperKit.iss") "/DAppVersion=$Version" "/DPayloadDir=$payloadDir"
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }
} else {
    Write-Host "  ISCC.exe not installed on this machine; compiled native setup executable '$setupExe' + DeveloperKit.iss script."
}

if (-not $SkipSelfTest) {
    Write-Host "[5/5] Verifying Developer Kit installer lifecycle (install -> verify -> upgrade -> verify -> uninstall)..."
    $testDir = Join-Path $env:TEMP ("nsk-devkit-test-" + [guid]::NewGuid().ToString("N"))
    try {
        & $setupExe --install --install-dir $testDir --payload-dir $payloadDir --skip-dotnet-registration
        if ($LASTEXITCODE -ne 0) { throw "--install failed ($LASTEXITCODE)." }

        & $setupExe --verify --install-dir $testDir
        if ($LASTEXITCODE -ne 0) { throw "--verify after install failed ($LASTEXITCODE)." }

        & $setupExe --upgrade --install-dir $testDir --payload-dir $payloadDir --skip-dotnet-registration
        if ($LASTEXITCODE -ne 0) { throw "--upgrade failed ($LASTEXITCODE)." }

        & $setupExe --verify --install-dir $testDir
        if ($LASTEXITCODE -ne 0) { throw "--verify after upgrade failed ($LASTEXITCODE)." }

        & $setupExe --uninstall --install-dir $testDir --skip-dotnet-registration
        if ($LASTEXITCODE -ne 0) { throw "--uninstall failed ($LASTEXITCODE)." }

        if (Test-Path $testDir) { throw "Uninstall left behind '$testDir'." }
    }
    finally {
        if (Test-Path $testDir) { Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Write-Host "Developer Kit ready at '$devKitRoot'."
