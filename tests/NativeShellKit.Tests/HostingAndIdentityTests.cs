using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Communication;
using NativeShellKit.Hosting.DependencyInjection;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Hosting.Settings;
using NativeShellKit.Notifications;
using NativeShellKit.Security;
using NativeShellKit.Windows.SingleInstance;
using Xunit;

namespace NativeShellKit.Tests;

public sealed class HostingAndIdentityTests
{
    private interface ITestDep;
    private sealed class SingletonDep : ITestDep, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class TransientDep : ITestDep;

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
    public void ServiceContainer_SupportsSingletonTransientEnumerableAndDisposal()
    {
        var container = new ServiceContainer();
        container.AddSingleton<SingletonDep, SingletonDep>();
        container.AddSingleton<ITestDep>(sp => sp.GetRequiredService<SingletonDep>());
        container.AddTransient<TransientDep>(_ => new TransientDep());
        container.AddTransient<ITestDep, TransientDep>();

        var firstSingleton = container.GetRequiredService<SingletonDep>();
        var secondSingleton = container.GetRequiredService<SingletonDep>();
        Assert.Same(firstSingleton, secondSingleton);

        var t1 = container.GetRequiredService<TransientDep>();
        var t2 = container.GetRequiredService<TransientDep>();
        Assert.NotSame(t1, t2);

        var all = container.GetServices<ITestDep>();
        Assert.Equal(2, all.Count);

        container.Dispose();
        Assert.True(firstSingleton.Disposed);
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
}
