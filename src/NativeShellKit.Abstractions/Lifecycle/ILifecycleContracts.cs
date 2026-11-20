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

public sealed record HostBuildOptions(
    bool ValidateScopes = true,
    bool ValidateOnBuild = false);

public interface IBackgroundTaskSupervisor : IAsyncDisposable
{
    event Action<string, Exception>? TaskFailed;
    int ActiveTaskCount { get; }
    IReadOnlyCollection<string> ActiveTaskNames => Array.Empty<string>();
    Task RunSupervisedAsync(string taskName, Func<CancellationToken, Task> work);
    Task RunSupervisedLoopAsync(string taskName, TimeSpan interval, Func<CancellationToken, Task> iteration, bool runImmediately = true) =>
        RunSupervisedAsync(taskName, async ct =>
        {
            if (runImmediately && !ct.IsCancellationRequested)
                await iteration(ct).ConfigureAwait(false);
            using var timer = new PeriodicTimer(interval > TimeSpan.Zero ? interval : TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await iteration(ct).ConfigureAwait(false);
        });
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
