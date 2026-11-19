using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Notifications.History;
using NativeShellKit.Notifications.Presenters;

namespace NativeShellKit.Notifications;

public static class NotificationHostExtensions
{
    public static NativeShellHostBuilder AddUnifiedNotifications(
        this NativeShellHostBuilder builder,
        INativeNotificationPresenter? customPresenter = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureServices(services =>
        {
            services.AddSingleton<JsonNotificationHistoryStore>(sp =>
                new JsonNotificationHistoryStore(
                    sp.GetRequiredService<AppIdentity>(),
                    maxStoredRecords: 500,
                    coalesceDiskWrites: true));
            services.AddSingleton<INotificationHistoryStore>(sp =>
                sp.GetRequiredService<JsonNotificationHistoryStore>());
            services.AddSingleton<ILifecycleHook>(sp =>
                new NotificationHistoryFlushHook(sp.GetRequiredService<JsonNotificationHistoryStore>()));

            if (customPresenter is not null)
            {
                services.AddSingleton<INativeNotificationPresenter>(customPresenter);
            }
            else
            {
                services.AddSingleton<INativeNotificationPresenter>(sp =>
                    new WindowsAppNotificationPresenter(
                        sp.GetRequiredService<AppIdentity>(),
                        onActivated: null,
                        logger: sp.GetService<ISdkLogger>()));
            }

            services.AddSingleton<INotificationService>(sp =>
            {
                var presenter = sp.GetRequiredService<INativeNotificationPresenter>();
                var svc = new UnifiedNotificationService(
                    sp.GetRequiredService<AppIdentity>(),
                    sp.GetRequiredService<INotificationHistoryStore>(),
                    presenter,
                    sp.GetService<INavigationService>(),
                    sp.GetService<IAppLifecycleService>(),
                    clock: null,
                    taskSupervisor: sp.GetService<IBackgroundTaskSupervisor>(),
                    logger: sp.GetService<ISdkLogger>());

                if (presenter is WindowsAppNotificationPresenter winPresenter)
                {
                    winPresenter.AsyncActivatedCallback = args => svc.DispatchPlatformActivationAsync(args);
                }

                return svc;
            });
        });
    }

    private sealed class NotificationHistoryFlushHook(JsonNotificationHistoryStore store) : ILifecycleHook
    {
        public int Order => -50;

        public Task OnStartingAsync(AppStartupContext context, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task OnStoppingAsync(AppShutdownReason reason, CancellationToken ct = default) =>
            store.FlushAsync(ct);
    }
}
