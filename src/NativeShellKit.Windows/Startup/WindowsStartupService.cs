using Microsoft.Win32;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Windows;

namespace NativeShellKit.Windows.Startup;

public interface IStartupRegistryBackend
{
    string? ReadValue(string valueName);
    void WriteValue(string valueName, string value);
    void DeleteValue(string valueName);
}

public sealed class Win32RegistryStartupBackend : IStartupRegistryBackend
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? ReadValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void WriteValue(string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true);
        key?.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

public sealed class InMemoryStartupRegistryBackend : IStartupRegistryBackend
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);

    public string? ReadValue(string valueName) =>
        _entries.TryGetValue(valueName, out var v) ? v : null;

    public void WriteValue(string valueName, string value) =>
        _entries[valueName] = value;

    public void DeleteValue(string valueName) =>
        _entries.Remove(valueName);
}

public sealed class WindowsStartupService : IStartupService
{
    private readonly AppIdentity _identity;
    private readonly IStartupRegistryBackend _backend;

    public WindowsStartupService(AppIdentity identity, IStartupRegistryBackend? backend = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _backend = backend ?? new Win32RegistryStartupBackend();
    }

    public string RegistryValueName => _identity.StartupRegistryValueName;

    public bool IsEnabled() =>
        !string.IsNullOrWhiteSpace(_backend.ReadValue(RegistryValueName));

    public string? GetRegisteredCommand() =>
        _backend.ReadValue(RegistryValueName);

    public void Enable(string executablePath, bool startMinimized = false)
    {
        var command = FormatStartupCommand(executablePath, startMinimized, _identity.EffectiveStartupArguments);
        _backend.WriteValue(RegistryValueName, command);
    }

    public void Disable()
    {
        _backend.DeleteValue(RegistryValueName);
    }

    public static string FormatStartupCommand(string executablePath, bool startMinimized = false) =>
        FormatStartupCommand(executablePath, startMinimized, "--autostart");

    public static string FormatStartupCommand(string executablePath, bool startMinimized, string? startupArguments)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path cannot be empty.", nameof(executablePath));

        var fullPath = Path.GetFullPath(executablePath.Trim());
        var baseArgs = string.IsNullOrWhiteSpace(startupArguments) ? "--autostart" : startupArguments.Trim();
        return startMinimized
            ? $"\"{fullPath}\" {baseArgs} --minimized"
            : $"\"{fullPath}\" {baseArgs}";
    }
}
