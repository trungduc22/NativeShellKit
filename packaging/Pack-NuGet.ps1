[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$OutputDir = "",
    [string]$AdditionalNuGetSource = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
[xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props") -Raw
$propsVersion = [string]$buildProps.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $propsVersion
}
if ($Version -ne $propsVersion) {
    throw "The requested version ($Version) must match Directory.Build.props ($propsVersion)."
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\packages"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Get-ChildItem -LiteralPath $OutputDir -Force | Select-Object -First 1) {
    throw "The package output directory is not empty. Choose a fresh directory to preserve immutable packages."
}

$projects = @(
    "src\NativeShellKit.Abstractions\NativeShellKit.Abstractions.csproj",
    "src\NativeShellKit.Hosting\NativeShellKit.Hosting.csproj",
    "src\NativeShellKit.Windows\NativeShellKit.Windows.csproj",
    "src\NativeShellKit.WinUI\NativeShellKit.WinUI.csproj",
    "src\NativeShellKit.DesignSystem\NativeShellKit.DesignSystem.csproj",
    "src\NativeShellKit.Notifications\NativeShellKit.Notifications.csproj",
    "src\NativeShellKit.Tray\NativeShellKit.Tray.csproj",
    "src\NativeShellKit.Security\NativeShellKit.Security.csproj",
    "src\NativeShellKit.Communication\NativeShellKit.Communication.csproj",
    "src\NativeShellKit.Diagnostics\NativeShellKit.Diagnostics.csproj",
    "src\NativeShellKit.Build\NativeShellKit.Build.csproj",
    "tools\NativeShellKit.DevTools\NativeShellKit.DevTools.csproj"
)
$templateProject = "templates\NativeShellKit.Templates\NativeShellKit.Templates.csproj"

Write-Host "[1/3] Restoring and building SDK libraries, DevTools, and Templates ($Configuration|$Platform)..."
foreach ($relProj in $projects) {
    $projPath = Join-Path $repoRoot $relProj
    if ([string]::IsNullOrWhiteSpace($AdditionalNuGetSource)) {
        & dotnet restore $projPath
    } else {
        & dotnet restore $projPath "-p:RestoreAdditionalProjectSources=$AdditionalNuGetSource"
    }
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed for $relProj ($LASTEXITCODE)." }

    & dotnet build $projPath -c $Configuration -p:Platform=$Platform -p:Version=$Version --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for $relProj ($LASTEXITCODE)." }
}

& dotnet restore (Join-Path $repoRoot $templateProject)
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed for $templateProject ($LASTEXITCODE)." }

Write-Host "[2/3] Packing NativeShellKit NuGet libraries and templates ($Version)..."
foreach ($relProj in $projects) {
    $projPath = Join-Path $repoRoot $relProj
    & dotnet pack $projPath -c $Configuration -p:Platform=$Platform -p:Version=$Version --no-build -o $OutputDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for $relProj ($LASTEXITCODE)." }
}
& dotnet pack (Join-Path $repoRoot $templateProject) -c $Configuration -p:Version=$Version --no-restore -o $OutputDir
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for $templateProject ($LASTEXITCODE)." }

Write-Host "[3/3] Validating generated .nupkg packages and writing SHA256SUMS.txt..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packages = Get-ChildItem -Path $OutputDir -Filter "NativeShellKit.*.nupkg" | Sort-Object Name
$expectedCount = $projects.Count + 1
if ($packages.Count -ne $expectedCount) {
    throw "Expected exactly $expectedCount packages in '$OutputDir', found $($packages.Count)."
}

$manifestItems = @()
$checksumLines = @()
foreach ($pkg in $packages) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($pkg.FullName)
    try {
        $entries = $zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
        $hasNuspec = ($entries | Where-Object { $_ -like "*.nuspec" }).Count -gt 0
        if ($pkg.Name -like "NativeShellKit.Templates.*") {
            $hasTemplateJson = ($entries | Where-Object { $_ -like "content/*template.json" }).Count -gt 0
            if (-not $hasNuspec -or -not $hasTemplateJson) {
                throw "Invalid Template NuGet archive '$($pkg.Name)': nuspec=$hasNuspec, templateJson=$hasTemplateJson"
            }
        }
        else {
            $hasDll = ($entries | Where-Object { $_ -like "lib/*.dll" }).Count -gt 0
            if (-not $hasNuspec -or -not $hasDll) {
                throw "Invalid NuGet archive '$($pkg.Name)': nuspec=$hasNuspec, dll=$hasDll"
            }
        }
        if ($pkg.Name -like "NativeShellKit.WinUI.*" -or $pkg.Name -like "NativeShellKit.DesignSystem.*") {
            $hasPri = ($entries | Where-Object { $_ -like "*.pri" }).Count -gt 0
            $hasXbf = ($entries | Where-Object { $_ -like "*.xbf" }).Count -gt 0
            if (-not $hasPri -or -not $hasXbf) {
                throw "WinUI NuGet archive '$($pkg.Name)' is missing compiled XAML resources (pri=$hasPri, xbf=$hasXbf)."
            }
        }
    }
    finally {
        $zip.Dispose()
    }

    $sha = (Get-FileHash -Path $pkg.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLines += "$sha  $($pkg.Name)"
    $manifestItems += @{
        fileName = $pkg.Name
        sizeBytes = $pkg.Length
        sha256 = $sha
    }
    Write-Host "  OK $($pkg.Name) ($($pkg.Length) bytes)"
}

Set-Content -Path (Join-Path $OutputDir "SHA256SUMS.txt") -Value $checksumLines -Encoding ASCII
@{
    version = $Version
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    packages = $manifestItems
} | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $OutputDir "feed-manifest.json") -Encoding UTF8

Write-Host "Packaged $($packages.Count) NuGet libraries to '$OutputDir'."
