using NativeShellKit.Abstractions.Lifecycle;

namespace NativeShellKit.Hosting.Hosting;

public sealed class AppLifecycleService : IAppLifecycleService
{
    public AppLifecycleService(AppStartupContext startupContext)
    {
        StartupContext = startupContext ?? throw new ArgumentNullException(nameof(startupContext));
    }

    public bool IsRunning { get; internal set; }
    public bool IsStarted { get; internal set; }
    public AppStartupContext StartupContext { get; }

    public event EventHandler<AppActivationEventArgs>? ActivationReceived;
    public event EventHandler<AppShutdownReason>? ShutdownRequested;

    public Task RaiseActivationAsync(AppActivationEventArgs args, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ct.ThrowIfCancellationRequested();
        ActivationReceived?.Invoke(this, args);
        return Task.CompletedTask;
    }

    public Task RequestShutdownAsync(AppShutdownReason reason = AppShutdownReason.UserRequested, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IsRunning = false;
        ShutdownRequested?.Invoke(this, reason);
        return Task.CompletedTask;
    }
}
