namespace NativeShellKit.Abstractions.Windows;

public interface IStartupService
{
    string RegistryValueName { get; }
    bool IsEnabled();
    string? GetRegisteredCommand();
    void Enable(string executablePath, bool startMinimized = false);
    void Disable();
}

public interface ISingleInstanceService : IAsyncDisposable
{
    string MutexName { get; }
    string PipeName { get; }
    bool IsPrimaryInstance { get; }
    event Action<string>? ActivationCommandReceived;

    bool TryAcquirePrimaryInstance();
    Task<bool> SignalExistingPrimaryInstanceAsync(
        string command = "SHOW_WINDOW",
        int timeoutMs = 1500,
        CancellationToken ct = default);
}
