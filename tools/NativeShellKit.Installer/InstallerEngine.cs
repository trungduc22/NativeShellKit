using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NativeShellKit.Build;

namespace NativeShellKit.Installer;

[Flags]
public enum DeveloperKitComponents
{
    None = 0,
    Packages = 1 << 0,
    CliAndTemplates = 1 << 1,
    Samples = 1 << 2,
    Documentation = 1 << 3,
    DotNetRegistration = 1 << 4,
    All = Packages | CliAndTemplates | Samples | Documentation | DotNetRegistration
}

public sealed record InstalledFileEntry(string RelativePath, string Sha256, long SizeBytes);

public sealed record DeveloperKitManifest(
    string SdkVersion,
    string Operation,
    DateTimeOffset InstalledUtc,
    string InstallDirectory,
    IReadOnlyList<InstalledFileEntry> Files,
    string Components = "Packages,CliAndTemplates,Samples,Documentation");

public sealed record InstallerOperationResult(bool Success, int ExitCode, string Message);

public static class InstallerEngine
{
    private static readonly string[] ManagedFolders = ["packages", "tools", "templates", "samples", "docs"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NativeShellKit", "DeveloperKit");

    public static string ResolveDefaultPayloadDir()
    {
        string nextToExe = Path.Combine(AppContext.BaseDirectory, "payload");
        if (Directory.Exists(nextToExe))
            return nextToExe;
        return Path.Combine(Directory.GetCurrentDirectory(), "payload");
    }

    public static bool IsAlreadyInstalled(string installDir) =>
        File.Exists(Path.Combine(installDir, "install-manifest.json"));

    public static DeveloperKitComponents ParseComponents(string? raw, bool skipRegistration)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return skipRegistration
                ? DeveloperKitComponents.All & ~DeveloperKitComponents.DotNetRegistration
                : DeveloperKitComponents.All;
        }

        var selected = DeveloperKitComponents.Packages;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "packages":
                    selected |= DeveloperKitComponents.Packages;
                    break;
                case "cli":
                case "templates":
                case "cli_templates":
                    selected |= DeveloperKitComponents.CliAndTemplates;
                    break;
                case "samples":
                    selected |= DeveloperKitComponents.Samples;
                    break;
                case "docs":
                case "documentation":
                    selected |= DeveloperKitComponents.Documentation;
                    break;
                case "register":
                    selected |= DeveloperKitComponents.DotNetRegistration;
                    break;
                case "all":
                    selected |= DeveloperKitComponents.All;
                    break;
            }
        }

        if (skipRegistration)
            selected &= ~DeveloperKitComponents.DotNetRegistration;

        return selected;
    }

    public static InstallerOperationResult InstallOrUpgrade(
        string payloadDir,
        string installDir,
        bool isUpgrade,
        DeveloperKitComponents components,
        Action<int, string>? onProgress = null)
    {
        components |= DeveloperKitComponents.Packages;

        if (!Directory.Exists(payloadDir))
            return new InstallerOperationResult(false, 2, $"Payload directory not found: {payloadDir}");

        string packagesSrc = Path.Combine(payloadDir, "packages");
        if (!Directory.Exists(packagesSrc) || Directory.GetFiles(packagesSrc, "NativeShellKit.*.nupkg").Length == 0)
            return new InstallerOperationResult(false, 3, $"Payload is missing NativeShellKit NuGet packages under: {packagesSrc}");

        var forbiddenInPayload = ReleasePackagingValidator.FindForbiddenArtifacts(payloadDir);
        if (forbiddenInPayload.Count > 0)
            return new InstallerOperationResult(false, 4, $"Forbidden artifact found in payload: {forbiddenInPayload[0]}");

        onProgress?.Invoke(10, "Preparing target directory...");
        Directory.CreateDirectory(installDir);
        string manifestPath = Path.Combine(installDir, "install-manifest.json");
        bool previouslyInstalled = File.Exists(manifestPath);

        if (isUpgrade || previouslyInstalled)
        {
            onProgress?.Invoke(20, "Cleaning previous Developer Kit payload folders...");
            foreach (var folder in ManagedFolders)
            {
                string existing = Path.Combine(installDir, folder);
                if (Directory.Exists(existing))
                    Directory.Delete(existing, recursive: true);
            }
        }

        var foldersToCopy = ResolveSelectedFolders(components);
        int step = 0;
        foreach (var folder in foldersToCopy)
        {
            step++;
            string srcFolder = Path.Combine(payloadDir, folder);
            if (!Directory.Exists(srcFolder))
                continue;

            int pct = 25 + (int)(50.0 * step / Math.Max(1, foldersToCopy.Count));
            onProgress?.Invoke(pct, $"Copying '{folder}' files...");
            CopyDirectory(srcFolder, Path.Combine(installDir, folder));
        }

        onProgress?.Invoke(80, "Computing SHA-256 verification manifest...");
        var entries = new List<InstalledFileEntry>();
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

        string version = typeof(InstallerEngine).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?
            .Split('+')[0] ?? "0.1.0-alpha.1";

        var manifest = new DeveloperKitManifest(
            version,
            isUpgrade ? "upgrade" : "install",
            DateTimeOffset.UtcNow,
            installDir,
            entries,
            components.ToString());

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        if (components.HasFlag(DeveloperKitComponents.DotNetRegistration))
        {
            onProgress?.Invoke(92, "Registering local NuGet feed and project templates...");
            TryRegisterDotNetIntegration(installDir, components.HasFlag(DeveloperKitComponents.CliAndTemplates));
        }

        onProgress?.Invoke(100, isUpgrade ? "Upgrade complete." : "Installation complete.");
        string label = isUpgrade ? "DEVELOPER_KIT_UPGRADED" : "DEVELOPER_KIT_INSTALLED";
        return new InstallerOperationResult(true, 0, $"{label} dir=\"{installDir}\" files={entries.Count} version={version}");
    }

    public static InstallerOperationResult Verify(string installDir)
    {
        string manifestPath = Path.Combine(installDir, "install-manifest.json");
        if (!File.Exists(manifestPath))
            return new InstallerOperationResult(false, 2, $"install-manifest.json missing in '{installDir}'.");

        var manifest = JsonSerializer.Deserialize<DeveloperKitManifest>(File.ReadAllText(manifestPath));
        if (manifest is null || manifest.Files.Count == 0)
            return new InstallerOperationResult(false, 3, "install-manifest.json is empty or unreadable.");

        foreach (var entry in manifest.Files)
        {
            string target = Path.Combine(installDir, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(target))
                return new InstallerOperationResult(false, 4, $"Missing installed file: {entry.RelativePath}");
            if (!string.Equals(ReleasePackagingValidator.ComputeSha256(target), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                return new InstallerOperationResult(false, 5, $"Checksum mismatch: {entry.RelativePath}");
        }

        string packagesDir = Path.Combine(installDir, "packages");
        var packages = Directory.Exists(packagesDir) ? Directory.GetFiles(packagesDir, "NativeShellKit.*.nupkg") : [];
        if (packages.Length < 11)
            return new InstallerOperationResult(false, 6, $"Expected >= 11 packages in '{packagesDir}', found {packages.Length}.");

        foreach (var pkg in packages)
        {
            string name = Path.GetFileName(pkg);
            bool isWinUi = name.StartsWith("NativeShellKit.WinUI.", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("NativeShellKit.DesignSystem.", StringComparison.OrdinalIgnoreCase);
            bool isTemplate = name.StartsWith("NativeShellKit.Templates.", StringComparison.OrdinalIgnoreCase);
            var res = ReleasePackagingValidator.ValidatePackageArchive(pkg, isWinUi, isTemplate);
            if (!res.IsValid)
                return new InstallerOperationResult(false, 7, $"{name}: {res.ErrorMessage}");
        }

        if (Directory.Exists(Path.Combine(installDir, "tools")))
        {
            string nskExe = Path.Combine(installDir, "tools", "nsk", "nsk.exe");
            if (!File.Exists(nskExe))
                return new InstallerOperationResult(false, 8, $"Missing nsk CLI at '{nskExe}'.");
        }

        var forbidden = ReleasePackagingValidator.FindForbiddenArtifacts(installDir);
        if (forbidden.Count > 0)
            return new InstallerOperationResult(false, 9, $"Forbidden artifact present: {forbidden[0]}");

        return new InstallerOperationResult(true, 0, $"DEVELOPER_KIT_VERIFIED dir=\"{installDir}\" packages={packages.Length} files={manifest.Files.Count}");
    }

    public static InstallerOperationResult Uninstall(string installDir, bool skipRegistration)
    {
        if (!Directory.Exists(installDir))
            return new InstallerOperationResult(true, 0, $"DEVELOPER_KIT_UNINSTALLED dir=\"{installDir}\" alreadyAbsent=true");

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

        return new InstallerOperationResult(true, 0, $"DEVELOPER_KIT_UNINSTALLED dir=\"{installDir}\" userDataPreserved=true");
    }

    private static IReadOnlyList<string> ResolveSelectedFolders(DeveloperKitComponents components)
    {
        var folders = new List<string> { "packages" };
        if (components.HasFlag(DeveloperKitComponents.CliAndTemplates))
        {
            folders.Add("tools");
            folders.Add("templates");
        }
        if (components.HasFlag(DeveloperKitComponents.Samples))
            folders.Add("samples");
        if (components.HasFlag(DeveloperKitComponents.Documentation))
            folders.Add("docs");
        return folders;
    }

    private static void TryRegisterDotNetIntegration(string installDir, bool installTemplates)
    {
        string packagesDir = Path.Combine(installDir, "packages");
        TryRunDotNet("nuget remove source NativeShellKitLocalFeed");
        TryRunDotNet($"nuget add source \"{packagesDir}\" --name NativeShellKitLocalFeed");

        string templatesDir = Path.Combine(installDir, "templates");
        if (installTemplates && Directory.Exists(templatesDir))
        {
            string? tplPkg = Directory.GetFiles(templatesDir, "NativeShellKit.Templates.*.nupkg").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(tplPkg))
            {
                TryRunDotNet("new uninstall NativeShellKit.Templates");
                TryRunDotNet($"new install \"{tplPkg}\"");
            }
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
            // Registration is best-effort when invoked without a .NET SDK on PATH.
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
}
