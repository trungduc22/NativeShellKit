using Microsoft.Extensions.DependencyInjection;
using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Abstractions.Windows;
using NativeShellKit.Communication;
using NativeShellKit.Hosting.DependencyInjection;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Hosting.Settings;
using NativeShellKit.Notifications;
using NativeShellKit.Security;
using NativeShellKit.Windows.SingleInstance;
using Xunit;
using NskServiceProviderExtensions = NativeShellKit.Abstractions.DependencyInjection.ServiceProviderExtensions;

namespace NativeShellKit.Tests;

public sealed class HostingAndIdentityTests
{
    private interface ITestDep;
    private sealed class SingletonDep : ITestDep, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class AsyncOwnedDep : IAsyncDisposable
    {
        public bool AsyncDisposed { get; private set; }
        public ValueTask DisposeAsync()
        {
            AsyncDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScopedCounterDep(List<string> trace, string label) : IDisposable
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public bool Disposed { get; private set; }
        public void Dispose()
        {
            Disposed = true;
            trace.Add(label);
        }
    }

    private sealed class TransientDep : ITestDep;

    private sealed class CircularA(CircularB b) { public CircularB B { get; } = b; }
    private sealed class CircularB(CircularA a) { public CircularA A { get; } = a; }

    private abstract class AbstractImpl : ITestDep;

    private sealed class OrderedHook(int order, List<string> log) : ILifecycleHook
    {
        public int Order => order;
        public Task OnStartingAsync(AppStartupContext context, CancellationToken ct = default)
        {
            log.Add($"start:{order}:{context.Identity.AppId}");
            return Task.CompletedTask;
        }
        public Task OnStoppingAsync(AppShutdownReason reason, CancellationToken ct = default)
        {
            log.Add($"stop:{order}:{reason}");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ServiceContainer_EnforcesLifetimesDisposalOwnershipAndAsyncDisposal()
    {
        var callerOwned = new SingletonDep();
        await using var container = new ServiceContainer();
        container.AddSingleton(callerOwned); // Caller retains disposal ownership
        container.AddSingleton<ITestDep, SingletonDep>();
        container.AddSingleton<AsyncOwnedDep, AsyncOwnedDep>();
        container.AddTransient<TransientDep>(_ => new TransientDep());

        Assert.Same(callerOwned, NskServiceProviderExtensions.GetRequiredService<SingletonDep>(container));
        var containerOwnedSingleton = NskServiceProviderExtensions.GetRequiredService<ITestDep>(container);
        Assert.Same(containerOwnedSingleton, NskServiceProviderExtensions.GetRequiredService<ITestDep>(container));

        var t1 = NskServiceProviderExtensions.GetRequiredService<TransientDep>(container);
        var t2 = NskServiceProviderExtensions.GetRequiredService<TransientDep>(container);
        Assert.NotSame(t1, t2);

        var asyncOwned = NskServiceProviderExtensions.GetRequiredService<AsyncOwnedDep>(container);
        Assert.False(asyncOwned.AsyncDisposed);

        await container.DisposeAsync();
        Assert.True(((SingletonDep)containerOwnedSingleton).Disposed);
        Assert.True(asyncOwned.AsyncDisposed);
        Assert.False(callerOwned.Disposed); // Must NOT dispose caller-owned instance
        callerOwned.Dispose();
    }

    [Fact]
    public async Task ServiceContainer_SupportsScopedLifetimeReverseDisposalAndMediInterop()
    {
        var trace = new List<string>();
        var container = new ServiceContainer();
        container.AddScoped(sp => new ScopedCounterDep(trace, "scoped"));
        container.AddSingleton<TransientDep, TransientDep>();

        var scopeFactory = NskServiceProviderExtensions.GetRequiredService<IServiceScopeFactory>(container);
        ScopedCounterDep? fromScope1;
        using (var scope1 = scopeFactory.CreateScope())
        {
            fromScope1 = NskServiceProviderExtensions.GetRequiredService<ScopedCounterDep>(scope1.ServiceProvider);
            var fromScope1Again = NskServiceProviderExtensions.GetRequiredService<ScopedCounterDep>(scope1.ServiceProvider);
            Assert.Same(fromScope1, fromScope1Again);
            Assert.False(fromScope1.Disposed);
        }
        Assert.True(fromScope1.Disposed);

        using var scope2 = scopeFactory.CreateScope();
        var fromScope2 = NskServiceProviderExtensions.GetRequiredService<ScopedCounterDep>(scope2.ServiceProvider);
        Assert.NotSame(fromScope1, fromScope2);

        await container.DisposeAsync();
        Assert.Contains("scoped", trace);
    }

    [Fact]
    public async Task ServiceContainer_DetectsCyclesValidatesRegistrationsAndHandlesConcurrentResolution()
    {
        await using var container = new ServiceContainer();
        Assert.Throws<ArgumentException>(() => container.AddSingleton<ITestDep, AbstractImpl>());

        container.AddTransient<CircularA, CircularA>();
        container.AddTransient<CircularB, CircularB>();
        Assert.Throws<InvalidOperationException>(() => NskServiceProviderExtensions.GetRequiredService<CircularA>(container));

        container.AddSingleton<SingletonDep, SingletonDep>();
        container.AddTransient<TransientDep, TransientDep>();

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
        {
            var s = NskServiceProviderExtensions.GetRequiredService<SingletonDep>(container);
            var t = NskServiceProviderExtensions.GetRequiredService<TransientDep>(container);
            return (s, t);
        })));

        Assert.All(results, r => Assert.Same(results[0].s, r.s));
        Assert.Equal(24, results.Select(r => r.t).Distinct().Count());
    }

    [Fact]
    public async Task HostBuilder_InteroperatesWithMicrosoftDependencyInjectionServiceCollection()
    {
        using var tmp = new TempRootScope("nsk-medi");
        var identity = new AppIdentity("mediapp", "MEDI App", baseDataRoot: tmp.RootPath);
        var disposeTrace = new List<string>();

        await using var host = NativeShellHostBuilder.Create(identity)
            .ConfigureServices(services =>
            {
                services.AddSingleton<TransientDep>();
                services.AddScoped(_ => new ScopedCounterDep(disposeTrace, "medi-scoped"));
            })
            .Build();

        await host.StartAsync();

        var scopeFactory = NskServiceProviderExtensions.GetRequiredService<IServiceScopeFactory>(host.Services);
        ScopedCounterDep scopedA;
        using (var scope = scopeFactory.CreateScope())
        {
            scopedA = NskServiceProviderExtensions.GetRequiredService<ScopedCounterDep>(scope.ServiceProvider);
            var singleton = NskServiceProviderExtensions.GetRequiredService<TransientDep>(scope.ServiceProvider);
            Assert.NotNull(singleton);
        }
        Assert.True(scopedA.Disposed);

        var exported = new ServiceCollection();
        ((IServiceRegistry)NskServiceProviderExtensions.GetRequiredService<IServiceRegistry>(host.Services)).CopyToServiceCollection(exported);
        Assert.Contains(exported, d => d.ServiceType == typeof(ScopedCounterDep) && d.Lifetime == ServiceLifetime.Scoped);

        await host.StopAsync();
    }

    [Fact]
    public async Task HostLifecycle_ExecutesHooksInOrder_AndIsolatesBackgroundTaskFaults()
    {
        using var tmp = new TempRootScope();
        var identity = new AppIdentity("hosttest", "Host Test", baseDataRoot: tmp.RootPath);
        var log = new List<string>();

        var builder = NativeShellHostBuilder.Create(identity, ["--self-test", "--autostart"])
            .ConfigureServices(s =>
            {
                s.AddSingleton<ILifecycleHook>(new OrderedHook(20, log));
                s.AddSingleton<ILifecycleHook>(new OrderedHook(5, log));
            });

        await using var host = builder.Build();
        Assert.True(host.Lifecycle.StartupContext.IsSelfTestMode);
        Assert.True(host.Lifecycle.StartupContext.StartedFromAutoStart);

        await host.StartAsync();
        Assert.Equal(["start:5:hosttest", "start:20:hosttest"], log);
        Assert.True(host.Lifecycle.IsRunning);

        var faultObserved = new TaskCompletionSource<string>();
        host.BackgroundTasks.TaskFailed += (name, _) => faultObserved.TrySetResult(name);
        await host.BackgroundTasks.RunSupervisedAsync("faulty-worker", _ => throw new InvalidOperationException("boom"));

        var failedName = await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("faulty-worker", failedName);
        Assert.True(host.Lifecycle.IsRunning);

        await host.StopAsync(AppShutdownReason.UserRequested);
        Assert.False(host.Lifecycle.IsRunning);
        Assert.Equal(
            ["start:5:hosttest", "start:20:hosttest", "stop:20:UserRequested", "stop:5:UserRequested"],
            log);
    }

    [Fact]
    public async Task AppIdentities_GhostCodeAndHiberDesk_RunSimultaneouslyWithZeroCollision()
    {
        using var tmp = new TempRootScope("nsk-multiapp");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var ghost = new AppIdentity("ghostcode", "GhostCode", "trungduc22", "1.0.0", Path.Combine(tmp.RootPath, "ghost"));
        var hiber = new AppIdentity("hiberdesk", "HiberDesk", "trungduc22", "1.0.0", Path.Combine(tmp.RootPath, "hiber"));

        Assert.NotEqual(ghost.DataDirectory, hiber.DataDirectory);
        Assert.NotEqual(ghost.SingleInstanceMutexName, hiber.SingleInstanceMutexName);
        Assert.NotEqual(ghost.ActivationPipeName, hiber.ActivationPipeName);
        Assert.NotEqual(ghost.IpcPipeName, hiber.IpcPipeName);
        Assert.NotEqual(ghost.TrayIconGuid, hiber.TrayIconGuid);
        Assert.NotEqual(ghost.InstallerGuid, hiber.InstallerGuid);

        await using var coordA = new SingleInstanceCoordinator(ghost, $"{ghost.SingleInstanceMutexName}_{suffix}", $"{ghost.ActivationPipeName}_{suffix}");
        await using var coordB = new SingleInstanceCoordinator(hiber, $"{hiber.SingleInstanceMutexName}_{suffix}", $"{hiber.ActivationPipeName}_{suffix}");

        Assert.True(coordA.TryAcquirePrimaryInstance());
        Assert.True(coordB.TryAcquirePrimaryInstance());

        await using var hostA = NativeShellHostBuilder.Create(ghost)
            .AddUnifiedNotifications()
            .ConfigureServices(s => s.AddCommunicationServices())
            .Build();
        await using var hostB = NativeShellHostBuilder.Create(hiber)
            .AddUnifiedNotifications()
            .ConfigureServices(s => s.AddCommunicationServices())
            .Build();

        await hostA.StartAsync();
        await hostB.StartAsync();

        var secretsA = new DpapiSecretStore(ghost);
        var secretsB = new DpapiSecretStore(hiber);
        await secretsA.SaveSecretAsync("token", "ghost-secret-value");
        await secretsB.SaveSecretAsync("token", "hiber-secret-value");
        Assert.Equal("ghost-secret-value", await secretsA.TryLoadSecretAsync("token"));
        Assert.Equal("hiber-secret-value", await secretsB.TryLoadSecretAsync("token"));

        var settingsA = new JsonFileSettingsStore<Dictionary<string, string>>(ghost.SettingsFilePath);
        var settingsB = new JsonFileSettingsStore<Dictionary<string, string>>(hiber.SettingsFilePath);
        await settingsA.SaveAsync(new Dictionary<string, string> { ["app"] = "ghost" });
        await settingsB.SaveAsync(new Dictionary<string, string> { ["app"] = "hiber" });

        Assert.Equal("ghost", (await settingsA.LoadAsync())["app"]);
        Assert.Equal("hiber", (await settingsB.LoadAsync())["app"]);

        await hostB.StopAsync();
        await hostA.StopAsync();
    }

    private sealed class CaptiveRootConsumer(ScopedCounterDep scoped)
    {
        public ScopedCounterDep Scoped { get; } = scoped;
    }

    private sealed class FaultingModule(string id, bool isCritical, int order = 100, params string[] deps) : IShellModule
    {
        public ModuleDescriptor Descriptor { get; } = new(id, id, "1.0.0");
        public int StartupOrder => order;
        public bool IsCritical => isCritical;
        public IReadOnlyList<string> DependsOnModuleIds => deps;

        public void RegisterServices(IServiceRegistry services) { }
        public IReadOnlyList<ModuleNavigationItem> GetNavigationItems() => [];
        public IReadOnlyList<ModuleCommandDescriptor> GetCommands() => [];
        public Task InitializeAsync(IModuleContext context, CancellationToken ct = default) =>
            throw new InvalidOperationException($"failure-in-{id}");
        public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class OrderedDependencyModule(string id, int order, List<string> trace, params string[] deps) : IShellModule
    {
        public ModuleDescriptor Descriptor { get; } = new(id, id, "1.0.0");
        public int StartupOrder => order;
        public IReadOnlyList<string> DependsOnModuleIds => deps;

        public void RegisterServices(IServiceRegistry services) { }
        public IReadOnlyList<ModuleNavigationItem> GetNavigationItems() => [];
        public IReadOnlyList<ModuleCommandDescriptor> GetCommands() => [];
        public Task InitializeAsync(IModuleContext context, CancellationToken ct = default)
        {
            trace.Add(id);
            return Task.CompletedTask;
        }
        public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task MediContainer_RejectsRootScopedCaptiveResolution_AndResolvesCrossThreadFactoriesWithoutDeadlock()
    {
        var trace = new List<string>();
        await using var container = new ServiceContainer();
        container.AddScoped(_ => new ScopedCounterDep(trace, "scoped"));
        container.AddSingleton<TransientDep, TransientDep>();
        container.AddSingleton(sp =>
        {
            // Cross-thread resolution inside factory would deadlock old ServiceContainer lock (_sync)
            var inner = Task.Run(() => NskServiceProviderExtensions.GetRequiredService<TransientDep>(sp))
                .GetAwaiter()
                .GetResult();
            return new SingletonDep();
        });

        Assert.Throws<InvalidOperationException>(() =>
            NskServiceProviderExtensions.GetRequiredService<ScopedCounterDep>(container));

        var resolved = await Task.Run(() =>
            NskServiceProviderExtensions.GetRequiredService<SingletonDep>(container))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(resolved);
    }

    [Fact]
    public async Task ModularHost_IsolatesNonCriticalFaults_EnforcesDependenciesAndStartupOrder()
    {
        using var tmp = new TempRootScope("nsk-mod-v2");
        var identity = new AppIdentity("modv2", "Mod V2", baseDataRoot: tmp.RootPath);
        var trace = new List<string>();

        var coreLateOrder = new OrderedDependencyModule("core-base", 50, trace);
        var leafEarlyOrderButDependsOnCore = new OrderedDependencyModule("core-leaf", 10, trace, "core-base");
        var optionalBroken = new FaultingModule("companion-bad", isCritical: false, order: 20);
        var downstreamDegraded = new OrderedDependencyModule("companion-child", 30, trace, "companion-bad");

        await using var host = NativeShellHostBuilder.Create(identity)
            .AddModule(leafEarlyOrderButDependsOnCore)
            .AddModule(downstreamDegraded)
            .AddModule(optionalBroken)
            .AddModule(coreLateOrder)
            .Build();

        await host.StartAsync();

        // core-base must start before core-leaf despite StartupOrder 50 vs 10
        Assert.Equal(["core-base", "core-leaf"], trace);
        Assert.Equal(ModuleHealthState.Healthy, host.Modules.GetHealthSnapshot("core-base")?.State);
        Assert.Equal(ModuleHealthState.Healthy, host.Modules.GetHealthSnapshot("core-leaf")?.State);
        Assert.Equal(ModuleHealthState.Faulted, host.Modules.GetHealthSnapshot("companion-bad")?.State);
        Assert.Equal(ModuleHealthState.Degraded, host.Modules.GetHealthSnapshot("companion-child")?.State);

        int ticks = 0;
        using var loopGate = new ManualResetEventSlim(false);
        var loopTask = host.BackgroundTasks.RunSupervisedLoopAsync(
            "heartbeat-loop",
            TimeSpan.FromMilliseconds(20),
            _ =>
            {
                if (Interlocked.Increment(ref ticks) >= 2)
                    loopGate.Set();
                return Task.CompletedTask;
            });

        Assert.True(loopGate.Wait(TimeSpan.FromSeconds(3)));
        Assert.Contains("heartbeat-loop", host.BackgroundTasks.ActiveTaskNames);

        await host.StopAsync();

        // Critical module failure must throw
        var criticalId = new AppIdentity("modcrit", "Mod Crit", baseDataRoot: Path.Combine(tmp.RootPath, "crit"));
        await using var critHost = NativeShellHostBuilder.Create(criticalId)
            .AddModule(new FaultingModule("crit-mod", isCritical: true))
            .Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => critHost.StartAsync());
    }

    [Fact]
    public void LegacyIdentityOverrides_PreserveExistingMutexPipeAndStartupRunNames()
    {
        using var tmp = new TempRootScope("nsk-legacy");
        var overrides = new LegacyIdentityOverrides(
            SingleInstanceMutexName: @"Local\Legacy_App_Mutex_v1",
            ActivationPipeName: "Legacy_App_Pipe_v1",
            StartupRegistryValueName: "LegacyAppRun",
            StartupArguments: "--background",
            AppUserModelIdOverride: "Legacy.Company.App");

        var identity = new AppIdentity("legacyapp", "Legacy App", "trungduc22", "1.0.0", tmp.RootPath, overrides);
        Assert.Equal(@"Local\Legacy_App_Mutex_v1", identity.SingleInstanceMutexName);
        Assert.Equal("Legacy_App_Pipe_v1", identity.ActivationPipeName);
        Assert.Equal("LegacyAppRun", identity.StartupRegistryValueName);
        Assert.Equal("--background", identity.EffectiveStartupArguments);
        Assert.Equal("Legacy.Company.App", identity.AppUserModelId);

        var backend = new NativeShellKit.Windows.Startup.InMemoryStartupRegistryBackend();
        var startup = new NativeShellKit.Windows.Startup.WindowsStartupService(identity, backend);
        startup.Enable(@"C:\Apps\Legacy\LegacyApp.exe", startMinimized: true);
        Assert.Contains("--background --minimized", startup.GetRegisteredCommand());
        Assert.NotNull(backend.ReadValue("LegacyAppRun"));

        var single = new SingleInstanceCoordinator(identity);
        Assert.Equal(@"Local\Legacy_App_Mutex_v1", single.MutexName);
        Assert.Equal("Legacy_App_Pipe_v1", single.PipeName);
    }
}
