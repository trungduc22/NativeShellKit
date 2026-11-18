using System.Text.RegularExpressions;

namespace NativeShellKit.Security;

public static partial class SafePathValidator
{
    private static readonly HashSet<string> ReservedWin32Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string ValidateSafeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Secret or file key cannot be empty.", nameof(key));

        var trimmed = key.Trim();
        if (trimmed.Length > 64 ||
            trimmed.Contains("..", StringComparison.Ordinal) ||
            !SafeKeyRegex().IsMatch(trimmed) ||
            ReservedWin32Names.Contains(trimmed))
        {
            throw new ArgumentException($"Unsafe key '{key}'. Must match [a-zA-Z0-9._-] and avoid reserved device names.", nameof(key));
        }

        return trimmed;
    }

    public static string ResolveUnderRoot(string rootDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Root directory cannot be empty.", nameof(rootDirectory));
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path cannot be empty.", nameof(relativePath));
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Path must be relative to root.", nameof(relativePath));
        if (relativePath.Contains(':', StringComparison.Ordinal))
            throw new ArgumentException("Drive letters and alternate data streams are not permitted.", nameof(relativePath));

        var fullRoot = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(fullRoot, relativePath));

        if (!combined.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Resolved path '{combined}' escapes root '{fullRoot}'.");

        foreach (var part in relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            var baseName = Path.GetFileNameWithoutExtension(part);
            if (part is "." or ".." || ReservedWin32Names.Contains(part) || ReservedWin32Names.Contains(baseName))
                throw new InvalidOperationException($"Path contains forbidden segment '{part}'.");
        }

        return combined;
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._-]*$")]
    private static partial Regex SafeKeyRegex();
}
