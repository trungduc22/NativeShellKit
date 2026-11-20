using System.Reflection;
using System.Text;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Build;
using NativeShellKit.Windows.Platform;

namespace NativeShellKit.DevTools;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return 0;
        }

        string cmd = args[0].ToLowerInvariant();
        return cmd switch
        {
            "info" => RunInfo(),
            "doctor" => RunDoctor(args[1..]),
            "validate-identity" => RunValidateIdentity(args[1..]),
            "generate-installer" => RunGenerateInstaller(args[1..]),
            _ => UnknownCommand(cmd)
        };
    }

    private static int RunInfo()
    {
        var ver = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.2.0-alpha.4";
        Console.WriteLine($"NativeShellKit CLI (nsk) {ver}");
        Console.WriteLine($"OS: {Environment.OSVersion.VersionString} (x64={Environment.Is64BitProcess})");
        Console.WriteLine($"Runtime: {Environment.Version}");
        return 0;
    }

    private static int RunDoctor(string[] args)
    {
        var win = new WindowsVersionService().GetCurrent();
        Console.WriteLine($"[check] Windows Build: {win.BuildNumber} (Win10+={win.IsWindows10OrLater}, Win11+={win.IsWindows11OrLater}, Mica={win.SupportsMica})");
        Console.WriteLine($"[check] Architecture: {(win.Is64BitProcess ? "x64" : "other")}");

        string? feedDir = GetArgValue(args, "--feed");
        if (!string.IsNullOrWhiteSpace(feedDir))
        {
            if (!Directory.Exists(feedDir))
            {
                Console.Error.WriteLine($"[fail] Feed directory not found: {feedDir}");
                return 2;
            }
            var packages = Directory.GetFiles(feedDir, "NativeShellKit.*.nupkg");
            Console.WriteLine($"[check] Packages in feed '{feedDir}': {packages.Length}");
            foreach (var p in packages)
            {
                string fileName = Path.GetFileName(p);
                bool isWinUi = fileName.StartsWith("NativeShellKit.WinUI.", StringComparison.OrdinalIgnoreCase) ||
                               fileName.StartsWith("NativeShellKit.DesignSystem.", StringComparison.OrdinalIgnoreCase);
                bool isTemplate = fileName.StartsWith("NativeShellKit.Templates.", StringComparison.OrdinalIgnoreCase);
                var result = ReleasePackagingValidator.ValidatePackageArchive(p, isWinUi, isTemplate);
                if (!result.IsValid)
                {
                    Console.Error.WriteLine($"[fail] {fileName}: {result.ErrorMessage}");
                    return 3;
                }
            }
        }

        Console.WriteLine("DOCTOR_OK");
        return 0;
    }

    private static int RunValidateIdentity(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: nsk validate-identity <appId> <displayName> [publisher]");
            return 1;
        }

        string publisher = args.Length >= 3 ? args[2] : "NativeShellKit";
        var id = new AppIdentity(args[0], args[1], publisher);
        Console.WriteLine($"AppId: {id.AppId}");
        Console.WriteLine($"AppUserModelId: {id.AppUserModelId}");
        Console.WriteLine($"DataDirectory: {id.DataDirectory}");
        Console.WriteLine($"Mutex: {id.SingleInstanceMutexName}");
        Console.WriteLine($"ActivationPipe: {id.ActivationPipeName}");
        Console.WriteLine($"IpcPipe: {id.IpcPipeName}");
        Console.WriteLine($"StartupRunKey: {id.StartupRegistryValueName}");
        Console.WriteLine($"TrayIconGuid: {id.TrayIconGuid}");
        Console.WriteLine($"InstallerGuid: {id.InstallerGuid}");
        return 0;
    }

    private static int RunGenerateInstaller(string[] args)
    {
        string? appId = GetArgValue(args, "--app-id");
        string? appName = GetArgValue(args, "--app-name");
        string? exeName = GetArgValue(args, "--exe");
        string? outDir = GetArgValue(args, "--out");
        string publisher = GetArgValue(args, "--publisher") ?? "NativeShellKit";
        string version = GetArgValue(args, "--version") ?? "0.1.0";

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appName) ||
            string.IsNullOrWhiteSpace(exeName) || string.IsNullOrWhiteSpace(outDir))
        {
            Console.Error.WriteLine("Usage: nsk generate-installer --app-id <id> --app-name <name> --exe <exe> --out <dir>");
            return 1;
        }

        var id = new AppIdentity(appId, appName, publisher, version);
        Directory.CreateDirectory(outDir);

        string issPath = Path.Combine(outDir, $"{id.AppId}.iss");
        string installPs1Path = Path.Combine(outDir, $"Install-{id.AppId}.ps1");
        string uninstallPs1Path = Path.Combine(outDir, $"Uninstall-{id.AppId}.ps1");
        string innoAppId = "{{" + id.InstallerGuid.ToString("D").ToUpperInvariant() + "}";

        string iss = $$"""
#ifndef AppVersion
  #define AppVersion "{{version}}"
#endif
#ifndef PayloadDir
  #define PayloadDir ".\payload"
#endif

[Setup]
AppId={{innoAppId}}
AppName={{id.DisplayName}}
AppVersion={#AppVersion}
AppPublisher={{id.Publisher}}
DefaultDirName={localappdata}\Programs\{{id.AppId}}
DefaultGroupName={{id.DisplayName}}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename={{id.AppId}}-Setup-x64
Compression=lzma2/normal
SolidCompression=yes

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.dpapi,*.db,*.db-wal,*.db-shm,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{{id.DisplayName}}"; Filename: "{app}\{{exeName}}"; WorkingDir: "{app}"
""";

        string installPs1 = $$"""
param([string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\{{id.AppId}}"))
$ErrorActionPreference = "Stop"
$payload = Join-Path $PSScriptRoot "payload"
if (-not (Test-Path (Join-Path $payload "{{exeName}}"))) { throw "Missing {{exeName}} under $payload" }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $payload "*") -Destination $InstallDir -Recurse -Force
Write-Host "Installed {{id.DisplayName}} to $InstallDir"
""";

        string uninstallPs1 = $$"""
param([string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\{{id.AppId}}"))
$ErrorActionPreference = "Stop"
if (Test-Path $InstallDir) { Remove-Item -Path $InstallDir -Recurse -Force }
Write-Host "Removed {{id.DisplayName}} from $InstallDir (user data preserved in {{id.DataDirectory}})"
""";

        File.WriteAllText(issPath, iss, Encoding.UTF8);
        File.WriteAllText(installPs1Path, installPs1, Encoding.UTF8);
        File.WriteAllText(uninstallPs1Path, uninstallPs1, Encoding.UTF8);

        Console.WriteLine($"Generated installer scripts in '{outDir}':");
        Console.WriteLine($"  - {issPath}");
        Console.WriteLine($"  - {installPs1Path}");
        Console.WriteLine($"  - {uninstallPs1Path}");
        return 0;
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("NativeShellKit CLI (nsk)");
        Console.WriteLine("  nsk info");
        Console.WriteLine("  nsk doctor [--feed <packagesDir>]");
        Console.WriteLine("  nsk validate-identity <appId> <displayName> [publisher]");
        Console.WriteLine("  nsk generate-installer --app-id <id> --app-name <name> --exe <exe> --out <dir>");
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintUsage();
        return 1;
    }
}
