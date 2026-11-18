using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace NativeShellKit.Build;

public sealed record PackageValidationResult(
    string PackagePath,
    bool IsValid,
    bool HasNuspec,
    bool HasAssembly,
    bool HasWinUiResources,
    string ErrorMessage);

public static class ReleasePackagingValidator
{
    private static readonly string[] ForbiddenExtensions = [".dpapi", ".db", ".db-wal", ".db-shm", ".pfx", ".snk", ".key", ".pem"];
    private static readonly string[] ForbiddenNames = ["pairing.generated.json", "secrets.json", "credentials.json"];

    public static PackageValidationResult ValidatePackageArchive(string nupkgPath, bool expectWinUiResources = false)
    {
        if (string.IsNullOrWhiteSpace(nupkgPath) || !File.Exists(nupkgPath))
            return new PackageValidationResult(nupkgPath, false, false, false, false, "Package file not found.");

        try
        {
            using var archive = ZipFile.OpenRead(nupkgPath);
            var entries = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();

            bool hasNuspec = entries.Any(e => e.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            bool hasDll = entries.Any(e => e.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) &&
                                           e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            bool hasPri = entries.Any(e => e.EndsWith(".pri", StringComparison.OrdinalIgnoreCase));
            bool hasXbf = entries.Any(e => e.EndsWith(".xbf", StringComparison.OrdinalIgnoreCase));
            bool hasWinUiResources = !expectWinUiResources || (hasPri && hasXbf);

            bool ok = hasNuspec && hasDll && hasWinUiResources;
            string err = ok
                ? string.Empty
                : $"Missing expected archive contents (nuspec={hasNuspec}, dll={hasDll}, pri={hasPri}, xbf={hasXbf}).";

            return new PackageValidationResult(nupkgPath, ok, hasNuspec, hasDll, hasPri && hasXbf, err);
        }
        catch (Exception ex)
        {
            return new PackageValidationResult(nupkgPath, false, false, false, false, ex.Message);
        }
    }

    public static IReadOnlyList<string> FindForbiddenArtifacts(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            return Array.Empty<string>();

        var leaks = new List<string>();
        foreach (var file in Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            var name = Path.GetFileName(file);
            if (ForbiddenExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) ||
                ForbiddenNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                leaks.Add(file);
            }
        }
        return leaks;
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task WriteChecksumsAsync(IEnumerable<string> filePaths, string outputFile, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        foreach (var path in filePaths)
        {
            if (!File.Exists(path))
                continue;
            var hash = ComputeSha256(path);
            sb.AppendLine($"{hash}  {Path.GetFileName(path)}");
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputFile));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputFile, sb.ToString(), Encoding.ASCII, ct).ConfigureAwait(false);
    }
}
