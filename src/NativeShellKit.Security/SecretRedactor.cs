using System.Text.RegularExpressions;

namespace NativeShellKit.Security;

public static partial class SecretRedactor
{
    public const string RedactedPlaceholder = "[REDACTED]";

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        string result = BearerRegex().Replace(input, "$1" + RedactedPlaceholder);
        result = KeyValueSecretRegex().Replace(result, "$1" + RedactedPlaceholder);
        result = KnownTokenPrefixRegex().Replace(result, RedactedPlaceholder);
        return result;
    }

    [GeneratedRegex(@"(Bearer\s+)[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"((?:api[_-]?key|client[_-]?secret|access[_-]?token|refresh[_-]?token|password|passwd|secret)\s*[:=]\s*[""']?)([^\s""',;\}]+)", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecretRegex();

    [GeneratedRegex(@"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{16,}\b|\bAIza[0-9A-Za-z\-_]{20,}\b|\bsk-[A-Za-z0-9\-_]{16,}\b")]
    private static partial Regex KnownTokenPrefixRegex();
}
