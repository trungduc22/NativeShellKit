using NativeShellKit.Abstractions.Identity;

namespace NativeShellKit.Abstractions.Lifecycle;

public enum AppShutdownReason
{
    UserRequested,
    SystemSessionEnding,
    UpdateRequested,
    UnhandledError,
    HostDisposed
}

public enum AppActivationKind
{
    PrimaryLaunch,
    SecondaryInstanceSignal,
    NotificationAction,
    ProtocolUri,
    BackgroundAutoStart
}

public sealed record AppStartupContext(
    AppIdentity Identity,
    string[] CommandLineArgs,
    bool StartedFromAutoStart,
    bool IsSelfTestMode);

public sealed class AppActivationEventArgs : EventArgs
{
    public AppActivationEventArgs(AppActivationKind kind, string? route = null, string? payload = null)
    {
        Kind = kind;
        Route = route;
        Payload = payload;
    }

    public AppActivationKind Kind { get; }
    public string? Route { get; }
    public string? Payload { get; }
}

public interface ILifecycleHook
{
    int Order => 0;
    Task OnStartingAsync(AppStartupContext context, CancellationToken ct = default);
    Task OnStoppingAsync(AppShutdownReason reason, CancellationToken ct = default);
}

public interface IBackgroundTaskSupervisor : IAsyncDisposable
{
    event Action<string, Exception>? TaskFailed;
    int ActiveTaskCount { get; }
    Task RunSupervisedAsync(string taskName, Func<CancellationToken, Task> work);
    Task StopAllAsync(TimeSpan timeout);
}

public interface IAppLifecycleService
{
    bool IsRunning { get; }
    bool IsStarted { get; }
    AppStartupContext StartupContext { get; }
    event EventHandler<AppActivationEventArgs>? ActivationReceived;
    event EventHandler<AppShutdownReason>? ShutdownRequested;

    Task RaiseActivationAsync(AppActivationEventArgs args, CancellationToken ct = default);
    Task RequestShutdownAsync(AppShutdownReason reason = AppShutdownReason.UserRequested, CancellationToken ct = default);
}

public interface IAppHost : IAsyncDisposable
{
    AppIdentity Identity { get; }
    IServiceProvider Services { get; }
    IAppLifecycleService Lifecycle { get; }
    IBackgroundTaskSupervisor BackgroundTasks { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(AppShutdownReason reason = AppShutdownReason.UserRequested, CancellationToken ct = default);
}
