using Microsoft.Extensions.DependencyInjection;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Hosting.DependencyInjection;
using NativeShellKit.Hosting.Modules;

namespace NativeShellKit.Hosting.Hosting;

public sealed class NativeShellHost : IAppHost
{
    private readonly ServiceContainer _container;
    private readonly ServiceProvider _provider;
    private readonly ModuleCatalog _moduleCatalog;
    private readonly AppLifecycleService _lifecycleService;
    private readonly ISdkLogger? _logger;
    private readonly List<IShellModule> _initializedModules = [];
    private bool _started;
    private bool _stopped;
    private bool _exceptionHandlersAttached;

    internal NativeShellHost(
        AppIdentity identity,
        ServiceContainer container,
        ServiceProvider provider,
        ModuleCatalog moduleCatalog,
        AppLifecycleService lifecycleService,
        IBackgroundTaskSupervisor backgroundTasks,
        ISdkLogger? logger = null)
    {
        Identity = identity;
        _container = container;
        _provider = provider;
        _moduleCatalog = moduleCatalog;
        _lifecycleService = lifecycleService;
        BackgroundTasks = backgroundTasks;
        _logger = logger;
    }

    public AppIdentity Identity { get; }
    public IServiceProvider Services => _provider;
    public IAppLifecycleService Lifecycle => _lifecycleService;
    public IBackgroundTaskSupervisor BackgroundTasks { get; }
    public IModuleCatalog Modules => _moduleCatalog;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started)
            return;

        Identity.EnsureDirectoriesExist();
        AttachGlobalExceptionHandlers();

        var hooks = ServiceProviderServiceExtensions.GetServices<ILifecycleHook>(_provider)
            .OrderBy(h => h.Order)
            .ToArray();

        foreach (var hook in hooks)
        {
            await hook.OnStartingAsync(_lifecycleService.StartupContext, ct).ConfigureAwait(false);
        }

        var orderedModules = _moduleCatalog.GetStartupOrderedModules();
        foreach (var module in orderedModules)
        {
            string moduleId = module.Descriptor.ModuleId;
            string? blockedDependency = module.DependsOnModuleIds
                .FirstOrDefault(dep =>
                {
                    var depHealth = _moduleCatalog.GetHealthSnapshot(dep)?.State;
                    return depHealth is ModuleHealthState.Faulted or ModuleHealthState.Disabled or ModuleHealthState.Degraded;
                });

            if (blockedDependency is not null)
            {
                string reason = $"Dependency module '{blockedDependency}' is unavailable.";
                if (module.IsCritical)
                    throw new InvalidOperationException($"Critical module '{moduleId}' cannot start: {reason}");

                _moduleCatalog.SetHealth(moduleId, ModuleHealthState.Degraded, reason);
                _logger?.Log(SdkLogLevel.Warning, nameof(NativeShellHost), $"Skipped non-critical module '{moduleId}': {reason}");
                continue;
            }

            _moduleCatalog.SetHealth(moduleId, ModuleHealthState.Starting);
            try
            {
                var dataDir = Identity.GetModuleDataDirectory(moduleId);
                var ctx = new ModuleContext(Identity, _provider, dataDir);
                await module.InitializeAsync(ctx, ct).ConfigureAwait(false);
                _moduleCatalog.SetHealth(moduleId, ModuleHealthState.Healthy);
                _initializedModules.Add(module);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _moduleCatalog.SetHealth(moduleId, ModuleHealthState.Stopped, "Startup canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _moduleCatalog.SetHealth(moduleId, ModuleHealthState.Faulted, ex.Message);
                _logger?.Log(SdkLogLevel.Error, nameof(NativeShellHost), $"Module '{moduleId}' initialization failed.", ex);
                if (module.IsCritical)
                    throw;
            }
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
            var module = _initializedModules[i];
            try
            {
                using var moduleStopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                moduleStopCts.CancelAfter(TimeSpan.FromSeconds(2));
                await module.ShutdownAsync(moduleStopCts.Token).ConfigureAwait(false);
                _moduleCatalog.SetHealth(module.Descriptor.ModuleId, ModuleHealthState.Stopped);
            }
            catch (Exception ex)
            {
                _logger?.Log(SdkLogLevel.Warning, nameof(NativeShellHost),
                    $"Module '{module.Descriptor.ModuleId}' shutdown failed cleanly: {ex.Message}", ex);
                _moduleCatalog.SetHealth(module.Descriptor.ModuleId, ModuleHealthState.Stopped, ex.Message);
            }
        }

        var hooks = ServiceProviderServiceExtensions.GetServices<ILifecycleHook>(_provider)
            .OrderByDescending(h => h.Order)
            .ToArray();

        foreach (var hook in hooks)
        {
            try
            {
                await hook.OnStoppingAsync(reason, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Log(SdkLogLevel.Warning, nameof(NativeShellHost), $"Lifecycle hook shutdown warning: {ex.Message}", ex);
            }
        }

        await BackgroundTasks.StopAllAsync(TimeSpan.FromSeconds(2.5)).ConfigureAwait(false);
        DetachGlobalExceptionHandlers();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
            await StopAsync(AppShutdownReason.HostDisposed).ConfigureAwait(false);

        await BackgroundTasks.DisposeAsync().ConfigureAwait(false);
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private void AttachGlobalExceptionHandlers()
    {
        if (_exceptionHandlersAttached)
            return;
        _exceptionHandlersAttached = true;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void DetachGlobalExceptionHandlers()
    {
        if (!_exceptionHandlersAttached)
            return;
        _exceptionHandlersAttached = false;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledDomainException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnUnhandledDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger?.Log(SdkLogLevel.Error, nameof(NativeShellHost),
                $"Unhandled AppDomain exception (IsTerminating={e.IsTerminating}).", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Log(SdkLogLevel.Error, nameof(NativeShellHost),
            "Unobserved task exception caught by NativeShellHost.", e.Exception);
        e.SetObserved();
    }
}
