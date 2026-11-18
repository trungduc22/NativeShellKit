using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NativeShellKit.Build;

namespace NativeShellKit.Installer;

public sealed record InstalledFileEntry(string RelativePath, string Sha256, long SizeBytes);

public sealed record DeveloperKitManifest(
    string SdkVersion,
    string Operation,
    DateTimeOffset InstalledUtc,
    string InstallDirectory,
    IReadOnlyList<InstalledFileEntry> Files);

public static class Program
{
    private static readonly string[] ManagedFolders = ["packages", "tools", "templates", "samples", "docs"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Main(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            PrintUsage();
            return 0;
        }

        string mode = ParseMode(args);
        string installDir = Path.GetFullPath(GetArgValue(args, "--install-dir") ?? DefaultInstallDir());
        string payloadDir = Path.GetFullPath(GetArgValue(args, "--payload-dir") ?? ResolveDefaultPayloadDir());
        bool skipRegistration = args.Any(a => string.Equals(a, "--skip-dotnet-registration", StringComparison.OrdinalIgnoreCase));

        return mode switch
        {
            "install" => RunInstallOrUpgrade(payloadDir, installDir, isUpgrade: false, skipRegistration),
            "upgrade" => RunInstallOrUpgrade(payloadDir, installDir, isUpgrade: true, skipRegistration),
            "verify" => RunVerify(installDir),
            "uninstall" => RunUninstall(installDir, skipRegistration),
            _ => UnknownMode(mode)
        };
    }

    private static string ParseMode(string[] args)
    {
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))) return "uninstall";
        if (args.Any(a => a.Equals("--verify", StringComparison.OrdinalIgnoreCase))) return "verify";
        if (args.Any(a => a.Equals("--upgrade", StringComparison.OrdinalIgnoreCase))) return "upgrade";
        return "install";
    }

    private static int RunInstallOrUpgrade(string payloadDir, string installDir, bool isUpgrade, bool skipRegistration)
    {
        if (!Directory.Exists(payloadDir))
        {
            Console.Error.WriteLine($"[fail] Payload directory not found: {payloadDir}");
            return 2;
        }

        string packagesSrc = Path.Combine(payloadDir, "packages");
        if (!Directory.Exists(packagesSrc) || Directory.GetFiles(packagesSrc, "NativeShellKit.*.nupkg").Length == 0)
        {
            Console.Error.WriteLine($"[fail] Payload is missing NativeShellKit NuGet packages under: {packagesSrc}");
            return 3;
        }

        var forbiddenInPayload = ReleasePackagingValidator.FindForbiddenArtifacts(payloadDir);
        if (forbiddenInPayload.Count > 0)
        {
            Console.Error.WriteLine($"[fail] Forbidden artifact found in payload: {forbiddenInPayload[0]}");
            return 4;
        }

        Directory.CreateDirectory(installDir);
        string manifestPath = Path.Combine(installDir, "install-manifest.json");
        bool previouslyInstalled = File.Exists(manifestPath);

        if (isUpgrade || previouslyInstalled)
        {
            foreach (var folder in ManagedFolders)
            {
                string existing = Path.Combine(installDir, folder);
                if (Directory.Exists(existing))
                    Directory.Delete(existing, recursive: true);
            }
        }

        var entries = new List<InstalledFileEntry>();
        foreach (var folder in ManagedFolders)
        {
            string srcFolder = Path.Combine(payloadDir, folder);
            if (!Directory.Exists(srcFolder))
                continue;

            string destFolder = Path.Combine(installDir, folder);
            CopyDirectory(srcFolder, destFolder);
        }

        foreach (var file in Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string rel = Path.GetRelativePath(installDir, file);
            if (rel.Equals("install-manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var topFolder = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (!ManagedFolders.Contains(topFolder, StringComparer.OrdinalIgnoreCase))
                continue;

            var info = new FileInfo(file);
            entries.Add(new InstalledFileEntry(rel.Replace('\\', '/'), ReleasePackagingValidator.ComputeSha256(file), info.Length));
        }

        string version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?
            .Split('+')[0] ?? "0.1.0-alpha.1";

        var manifest = new DeveloperKitManifest(
            version,
            isUpgrade ? "upgrade" : "install",
            DateTimeOffset.UtcNow,
            installDir,
            entries);

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        if (!skipRegistration)
        {
            TryRegisterDotNetIntegration(installDir);
        }

        Console.WriteLine($"DEVELOPER_KIT_{(isUpgrade ? "UPGRADED" : "INSTALLED")} dir=\"{installDir}\" files={entries.Count} version={version}");
        return 0;
    }

    private static int RunVerify(string installDir)
    {
        string manifestPath = Path.Combine(installDir, "install-manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"[fail] install-manifest.json missing in '{installDir}'.");
            return 2;
        }

        var manifest = JsonSerializer.Deserialize<DeveloperKitManifest>(File.ReadAllText(manifestPath));
        if (manifest is null || manifest.Files.Count == 0)
        {
            Console.Error.WriteLine("[fail] install-manifest.json is empty or unreadable.");
            return 3;
        }

        foreach (var entry in manifest.Files)
        {
            string target = Path.Combine(installDir, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(target))
            {
                Console.Error.WriteLine($"[fail] Missing installed file: {entry.RelativePath}");
                return 4;
            }
            if (!string.Equals(ReleasePackagingValidator.ComputeSha256(target), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[fail] Checksum mismatch: {entry.RelativePath}");
                return 5;
            }
        }

        string packagesDir = Path.Combine(installDir, "packages");
        var packages = Directory.Exists(packagesDir) ? Directory.GetFiles(packagesDir, "NativeShellKit.*.nupkg") : [];
        if (packages.Length < 11)
        {
            Console.Error.WriteLine($"[fail] Expected >= 11 packages in '{packagesDir}', found {packages.Length}.");
            return 6;
        }

        foreach (var pkg in packages)
        {
            string name = Path.GetFileName(pkg);
            bool isWinUi = name.StartsWith("NativeShellKit.WinUI.", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("NativeShellKit.DesignSystem.", StringComparison.OrdinalIgnoreCase);
            bool isTemplate = name.StartsWith("NativeShellKit.Templates.", StringComparison.OrdinalIgnoreCase);
            var res = ReleasePackagingValidator.ValidatePackageArchive(pkg, isWinUi, isTemplate);
            if (!res.IsValid)
            {
                Console.Error.WriteLine($"[fail] {name}: {res.ErrorMessage}");
                return 7;
            }
        }

        string nskExe = Path.Combine(installDir, "tools", "nsk", "nsk.exe");
        if (!File.Exists(nskExe))
        {
            Console.Error.WriteLine($"[fail] Missing nsk CLI at '{nskExe}'.");
            return 8;
        }

        var forbidden = ReleasePackagingValidator.FindForbiddenArtifacts(installDir);
        if (forbidden.Count > 0)
        {
            Console.Error.WriteLine($"[fail] Forbidden artifact present: {forbidden[0]}");
            return 9;
        }

        Console.WriteLine($"DEVELOPER_KIT_VERIFIED dir=\"{installDir}\" packages={packages.Length} files={manifest.Files.Count}");
        return 0;
    }

    private static int RunUninstall(string installDir, bool skipRegistration)
    {
        if (!Directory.Exists(installDir))
        {
            Console.WriteLine($"DEVELOPER_KIT_UNINSTALLED dir=\"{installDir}\" alreadyAbsent=true");
            return 0;
        }

        if (!skipRegistration)
        {
            TryRunDotNet("nuget remove source NativeShellKitLocalFeed");
        }

        foreach (var folder in ManagedFolders)
        {
            string target = Path.Combine(installDir, folder);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
        }

        string manifestPath = Path.Combine(installDir, "install-manifest.json");
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);

        if (!Directory.EnumerateFileSystemEntries(installDir).Any())
            Directory.Delete(installDir, recursive: false);

        Console.WriteLine($"DEVELOPER_KIT_UNINSTALLED dir=\"{installDir}\" userDataPreserved=true");
        return 0;
    }

    private static void TryRegisterDotNetIntegration(string installDir)
    {
        string packagesDir = Path.Combine(installDir, "packages");
        TryRunDotNet($"nuget remove source NativeShellKitLocalFeed");
        TryRunDotNet($"nuget add source \"{packagesDir}\" --name NativeShellKitLocalFeed");

        string? tplPkg = Directory.GetFiles(Path.Combine(installDir, "templates"), "NativeShellKit.Templates.*.nupkg")
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(tplPkg))
        {
            TryRunDotNet($"new uninstall NativeShellKit.Templates");
            TryRunDotNet($"new install \"{tplPkg}\"");
        }
    }

    private static void TryRunDotNet(string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("dotnet", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            proc?.WaitForExit(15000);
        }
        catch
        {
            // Registration is best-effort when invoked outside a full .NET SDK PATH
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
        }
    }

    private static string DefaultInstallDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NativeShellKit", "DeveloperKit");

    private static string ResolveDefaultPayloadDir()
    {
        string nextToExe = Path.Combine(AppContext.BaseDirectory, "payload");
        if (Directory.Exists(nextToExe))
            return nextToExe;
        return Path.Combine(Directory.GetCurrentDirectory(), "payload");
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
        Console.WriteLine("NativeShellKit Developer Kit Installer");
        Console.WriteLine("  --install [--install-dir <dir>] [--payload-dir <dir>] [--skip-dotnet-registration]");
        Console.WriteLine("  --upgrade [--install-dir <dir>] [--payload-dir <dir>] [--skip-dotnet-registration]");
        Console.WriteLine("  --verify  [--install-dir <dir>]");
        Console.WriteLine("  --uninstall [--install-dir <dir>] [--skip-dotnet-registration]");
    }

    private static int UnknownMode(string mode)
    {
        Console.Error.WriteLine($"Unknown mode: {mode}");
        return 1;
    }
}
