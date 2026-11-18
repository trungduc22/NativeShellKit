using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Diagnostics;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Notifications;
using NativeShellKit.Tray;
using NativeShellKit.Windows;
using NativeShellKit.Windows.SingleInstance;
using NativeShellKit.Windows.Startup;
using NativeShellKit.WinUI;
using WinRT;

namespace BackgroundCompanionApp;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool isSelfTest = args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase));
        string? tempRoot = isSelfTest ? Path.Combine(Path.GetTempPath(), $"nsk-companion-selftest-{Guid.NewGuid():N}") : null;
        string? mutexOverride = isSelfTest ? $"Local\\nsk_companion_selftest_{Guid.NewGuid():N}" : null;
        string? pipeOverride = isSelfTest ? $"nsk_companion_pipe_{Guid.NewGuid():N}" : null;

        try
        {
            var identity = new AppIdentity("backgroundcompanion", "Background Companion App", "NativeShellKit", "0.1.0", tempRoot);
            var coordinator = new SingleInstanceCoordinator(identity, mutexOverride, pipeOverride);
            try
            {
                bool acquiredPrimary = coordinator.TryAcquirePrimaryInstance();
                if (!isSelfTest && !acquiredPrimary)
                {
                    coordinator.SignalExistingPrimaryInstanceAsync("--activate").GetAwaiter().GetResult();
                    return 0;
                }

                IStartupRegistryBackend startupBackend = isSelfTest
                    ? new InMemoryStartupRegistryBackend()
                    : new Win32RegistryStartupBackend();

                var builder = NativeShellHostBuilder.Create(identity, args)
                    .AddWinUiShell()
                    .AddUnifiedNotifications()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(coordinator);
                        services.AddWindowsServices(startupBackend);
                        services.AddSingleton<NativeTrayService>(sp => new NativeTrayService(sp.GetRequiredService<AppIdentity>()));
                        services.AddSecurityAndDiagnostics();
                    });

                var host = builder.Build();
                try
                {
                    if (isSelfTest)
                    {
                        return RunSelfTestAsync(host, identity, acquiredPrimary, mutexOverride, pipeOverride)
                            .GetAwaiter().GetResult();
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
                coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
        AppIdentity identity,
        bool acquiredPrimary,
        string? mutexOverride,
        string? pipeOverride)
    {
        await host.StartAsync();

        await using var duplicateCoordinator = new SingleInstanceCoordinator(identity, mutexOverride, pipeOverride);
        bool blockedDuplicate = !duplicateCoordinator.TryAcquirePrimaryInstance();

        var startup = host.Services.GetRequiredService<WindowsStartupService>();
        startup.Enable(@"C:\Program Files\NativeShellKit\BackgroundCompanionApp.exe", startMinimized: true);
        bool startupEnabled = startup.IsEnabled();
        startup.Disable();
        bool startupDisabled = !startup.IsEnabled();

        var tray = host.Services.GetRequiredService<NativeTrayService>();
        var notifications = host.Services.GetRequiredService<INotificationService>();
        bool exitInvoked = false;
        MainWindow.ConfigureTrayMenu(tray, notifications, null, () => exitInvoked = true);

        int heartbeatTicks = 0;
        var heartbeatTask = host.BackgroundTasks.RunSupervisedAsync("heartbeat", async ct =>
        {
            while (!ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref heartbeatTicks);
                await Task.Delay(25, ct);
            }
        });

        await Task.Delay(80);

        var first = await notifications.PublishAsync(new AppNotificationRequest(
            "Companion Sync",
            "Initial sync completed.",
            NotificationSeverity.Success,
            DeduplicationKey: "sync-1"));

        var dup = await notifications.PublishAsync(new AppNotificationRequest(
            "Companion Sync",
            "Duplicate sync notice.",
            NotificationSeverity.Info,
            DeduplicationKey: "sync-1"));

        // Invoke tray menu item "Toggle Quiet Hours"
        tray.MenuItems.First(m => m.Text == "Toggle Quiet Hours").Callback?.Invoke();
        int currentHour = DateTimeOffset.Now.LocalDateTime.Hour;
        notifications.Preferences.QuietHoursStartHour = currentHour;
        notifications.Preferences.QuietHoursEndHour = (currentHour + 1) % 24;

        var quiet = await notifications.PublishAsync(new AppNotificationRequest(
            "Quiet Notice",
            "Should be suppressed during quiet hours.",
            NotificationSeverity.Info,
            DeduplicationKey: "quiet-1"));

        tray.MenuItems.First(m => m.Text == "Exit").Callback?.Invoke();

        await host.StopAsync();
        await heartbeatTask;

        bool ok = acquiredPrimary && blockedDuplicate &&
                  startupEnabled && startupDisabled &&
                  heartbeatTicks >= 1 &&
                  first.Persisted && first.PresentedInApp &&
                  dup.SuppressedReason == "Deduplicated" &&
                  quiet.SuppressedReason == "QuietHoursActive" &&
                  exitInvoked;

        Console.WriteLine(
            $"COMPANION_APP_SELF_TEST_PASSED primary={acquiredPrimary} blockedDup={blockedDuplicate} startup={startupEnabled}/{startupDisabled} ticks={heartbeatTicks} dedup={dup.SuppressedReason} quiet={quiet.SuppressedReason}");
        return ok ? 0 : 1;
    }
}
