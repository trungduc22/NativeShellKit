using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Communication;
using NativeShellKit.Diagnostics;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Notifications;
using NativeShellKit.WinUI;
using UnifiedHostDemo.Modules;
using WinRT;

namespace UnifiedHostDemo;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool isSelfTest = args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase));
        string? tempRoot = isSelfTest ? Path.Combine(Path.GetTempPath(), $"nsk-unified-selftest-{Guid.NewGuid():N}") : null;

        try
        {
            var identity = new AppIdentity("unifiedhostdemo", "Unified Host Demo", "NativeShellKit", "0.1.0", tempRoot);
            var archiveModule = new CodeArchiveCompanionModule();
            var sessionModule = new WorkspaceSessionModule();

            var builder = NativeShellHostBuilder.Create(identity, args)
                .AddWinUiShell()
                .AddUnifiedNotifications()
                .ConfigureServices(services =>
                {
                    services.AddSecurityAndDiagnostics();
                    services.AddCommunicationServices();
                })
                .AddModule(sessionModule)
                .AddModule(archiveModule);

            var host = builder.Build();
            try
            {
                if (isSelfTest)
                {
                    return RunSelfTestAsync(host, sessionModule, archiveModule).GetAwaiter().GetResult();
                }

                ComWrappersSupport.InitializeComWrappers();
                Application.Start(callbackParams =>
                {
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new App(host);
                });
                return 0;
            }
            finally
            {
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            if (tempRoot is not null && Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<int> RunSelfTestAsync(
        NativeShellHost host,
        WorkspaceSessionModule sessionModule,
        CodeArchiveCompanionModule archiveModule)
    {
        await host.StartAsync();

        var catalog = host.Services.GetRequiredService<IModuleCatalog>();
        var notifications = host.Services.GetRequiredService<INotificationService>();

        bool bothInitialized = sessionModule.IsInitialized && archiveModule.IsInitialized;
        bool executed = await catalog.ExecuteCommandAsync("sessions.snapshot", host.Services);
        var history = await notifications.GetHistoryAsync(10);

        await host.StopAsync();

        bool bothShutdown = sessionModule.IsShutdown && archiveModule.IsShutdown;
        bool ok = bothInitialized &&
                  executed &&
                  catalog.Modules.Count == 2 &&
                  catalog.GetAllNavigationItems().Count == 2 &&
                  sessionModule.State.CompletedSnapshots == 1 &&
                  archiveModule.State.LastIndexedCount == 42 &&
                  history.Any(h => h.SourceModuleId == archiveModule.Descriptor.ModuleId) &&
                  bothShutdown;

        Console.WriteLine(
            $"UNIFIED_HOST_DEMO_SELF_TEST_PASSED modules={catalog.Modules.Count} snapshots={sessionModule.State.CompletedSnapshots} indexed={archiveModule.State.LastIndexedCount} notifications={history.Count}");
        return ok ? 0 : 1;
    }
}
