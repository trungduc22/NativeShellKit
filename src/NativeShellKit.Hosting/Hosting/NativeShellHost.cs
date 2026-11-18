using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Hosting.DependencyInjection;
using NativeShellKit.Hosting.Modules;

namespace NativeShellKit.Hosting.Hosting;

public sealed class NativeShellHost : IAppHost
{
    private readonly ServiceContainer _container;
    private readonly ModuleCatalog _moduleCatalog;
    private readonly AppLifecycleService _lifecycleService;
    private readonly List<IShellModule> _initializedModules = [];
    private bool _started;
    private bool _stopped;

    internal NativeShellHost(
        AppIdentity identity,
        ServiceContainer container,
        ModuleCatalog moduleCatalog,
        AppLifecycleService lifecycleService,
        IBackgroundTaskSupervisor backgroundTasks)
    {
        Identity = identity;
        _container = container;
        _moduleCatalog = moduleCatalog;
        _lifecycleService = lifecycleService;
        BackgroundTasks = backgroundTasks;
    }

    public AppIdentity Identity { get; }
    public IServiceProvider Services => _container;
    public IAppLifecycleService Lifecycle => _lifecycleService;
    public IBackgroundTaskSupervisor BackgroundTasks { get; }
    public IModuleCatalog Modules => _moduleCatalog;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started)
            return;

        Identity.EnsureDirectoriesExist();

        var hooks = _container.GetServices<ILifecycleHook>()
            .OrderBy(h => h.Order)
            .ToArray();

        foreach (var hook in hooks)
        {
            await hook.OnStartingAsync(_lifecycleService.StartupContext, ct).ConfigureAwait(false);
        }

        foreach (var module in _moduleCatalog.Modules)
        {
            var dataDir = Identity.GetModuleDataDirectory(module.Descriptor.ModuleId);
            var ctx = new ModuleContext(Identity, _container, dataDir);
            await module.InitializeAsync(ctx, ct).ConfigureAwait(false);
            _initializedModules.Add(module);
        }

        _started = true;
        _lifecycleService.IsStarted = true;
        _lifecycleService.IsRunning = true;
    }

    public async Task StopAsync(AppShutdownReason reason = AppShutdownReason.UserRequested, CancellationToken ct = default)
    {
        if (_stopped)
            return;
        _stopped = true;
        _lifecycleService.IsRunning = false;

        for (int i = _initializedModules.Count - 1; i >= 0; i--)
        {
            try
            {
                await _initializedModules[i].ShutdownAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Continue shutting down remaining modules cleanly
            }
        }

        var hooks = _container.GetServices<ILifecycleHook>()
            .OrderByDescending(h => h.Order)
            .ToArray();

        foreach (var hook in hooks)
        {
            try
            {
                await hook.OnStoppingAsync(reason, ct).ConfigureAwait(false);
            }
            catch
            {
                // Continue executing shutdown hooks
            }
        }

        await BackgroundTasks.StopAllAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
            await StopAsync(AppShutdownReason.HostDisposed).ConfigureAwait(false);

        await BackgroundTasks.DisposeAsync().ConfigureAwait(false);
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}
