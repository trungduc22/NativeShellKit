using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;
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
            services.AddSingleton<INotificationHistoryStore>(sp =>
                new JsonNotificationHistoryStore(sp.GetRequiredService<AppIdentity>()));

            if (customPresenter is not null)
            {
                services.AddSingleton<INativeNotificationPresenter>(customPresenter);
            }
            else
            {
                services.AddSingleton<INativeNotificationPresenter>(sp =>
                    new WindowsAppNotificationPresenter(sp.GetRequiredService<AppIdentity>()));
            }

            services.AddSingleton<INotificationService>(sp =>
            {
                var presenter = sp.GetRequiredService<INativeNotificationPresenter>();
                var svc = new UnifiedNotificationService(
                    sp.GetRequiredService<AppIdentity>(),
                    sp.GetRequiredService<INotificationHistoryStore>(),
                    presenter,
                    sp.GetService<INavigationService>(),
                    sp.GetService<IAppLifecycleService>());

                if (presenter is WindowsAppNotificationPresenter winPresenter)
                {
                    winPresenter.ActivatedCallback = args => _ = svc.TryHandleActivationAsync(args);
                }

                return svc;
            });
        });
    }
}
