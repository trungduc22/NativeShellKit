using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NativeShellKit.Abstractions.Identity;

/// <summary>
/// Canonical application identity controlling storage isolation, IPC endpoints,
/// single-instance coordination, startup registration, and notification routing.
/// </summary>
public sealed partial class AppIdentity
{
    private readonly string _sanitizedId;

    public AppIdentity(
        string appId,
        string displayName,
        string publisher = "NativeShellKit",
        string version = "0.1.0",
        string? baseDataRoot = null)
    {
        AppId = ValidateSlug(appId, nameof(appId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName cannot be empty.", nameof(displayName));
        DisplayName = displayName.Trim();
        Publisher = ValidateSlug(publisher, nameof(publisher));
        Version = string.IsNullOrWhiteSpace(version) ? "0.1.0" : version.Trim();

        BaseDataRoot = string.IsNullOrWhiteSpace(baseDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.GetFullPath(baseDataRoot);

        _sanitizedId = SanitizeForIdentifier(AppId);
        AppUserModelId = $"{Publisher}.{AppId}";
        TrayIconGuid = DeriveDeterministicGuid($"tray:{AppUserModelId}");
        InstallerGuid = DeriveDeterministicGuid($"installer:{AppUserModelId}");
    }

    public string AppId { get; }
    public string DisplayName { get; }
    public string Publisher { get; }
    public string Version { get; }
    public string BaseDataRoot { get; }
    public string AppUserModelId { get; }
    public Guid TrayIconGuid { get; }
    public Guid InstallerGuid { get; }

    public string DataDirectory => Path.Combine(BaseDataRoot, Publisher, AppId);
    public string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public string SecretsDirectory => Path.Combine(DataDirectory, "secrets");
    public string NotificationHistoryFilePath => Path.Combine(DataDirectory, "notifications-history.json");

    public string SingleInstanceMutexName => $@"Local\NativeShellKit_{_sanitizedId}_SingleInstance_Mutex_v1";
    public string ActivationPipeName => $"NativeShellKit_{_sanitizedId}_Activation_Pipe_v1";
    public string IpcPipeName => $"NativeShellKit_{_sanitizedId}_Ipc_Pipe_v1";
    public string StartupRegistryValueName => $"NativeShellKit.{_sanitizedId}";

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(SecretsDirectory);
    }

    public string GetModuleDataDirectory(string moduleId)
    {
        var validated = ValidateSlug(moduleId, nameof(moduleId));
        var path = Path.Combine(DataDirectory, "modules", validated);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ValidateSlug(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);

        var trimmed = value.Trim();
        if (trimmed.Length > 64 || trimmed.Contains("..", StringComparison.Ordinal) || !SlugRegex().IsMatch(trimmed))
            throw new ArgumentException($"{paramName} must be 1..64 characters matching [a-zA-Z0-9._-] without '..'.", paramName);

        return trimmed;
    }

    private static string SanitizeForIdentifier(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        return sb.ToString();
    }

    private static Guid DeriveDeterministicGuid(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._-]*$")]
    private static partial Regex SlugRegex();
}
