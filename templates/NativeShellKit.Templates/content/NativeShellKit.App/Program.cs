using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Hosting.Hosting;
using NativeShellKit.Windows;
using NativeShellKit.WinUI;
using WinRT;
#if (EnableNotifications)
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Notifications;
#endif
#if (EnableTray)
using NativeShellKit.Tray;
#endif
#if (EnableDiagnostics)
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Diagnostics;
#endif

namespace NativeShellKitApp;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var identity = new AppIdentity("nativeshellkitapp", "NativeShellKitApp", "NativeShellKit", "0.1.0");
        var builder = NativeShellHostBuilder.Create(identity, args)
            .AddWinUiShell()
            .ConfigureServices(services =>
            {
                services.AddWindowsServices();
#if (EnableTray)
                services.AddSingleton<NativeTrayService>(sp => new NativeTrayService(sp.GetRequiredService<AppIdentity>()));
#endif
#if (EnableDiagnostics)
                services.AddSecurityAndDiagnostics();
#endif
            });

#if (EnableNotifications)
        builder.AddUnifiedNotifications();
#endif

        var host = builder.Build();
        try
        {
            if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                return RunSelfTestAsync(host).GetAwaiter().GetResult();
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

    private static async Task<int> RunSelfTestAsync(NativeShellHost host)
    {
        await host.StartAsync();
        var nav = host.Services.GetRequiredService<INavigationService>();
        var theme = host.Services.GetRequiredService<IThemeService>();
        nav.RegisterRoute(new NavigationRouteDescriptor("overview", "Overview", "\uE80F", () => new object()));
        bool navOk = nav.Navigate("overview");
        theme.ApplyTheme(AppThemeMode.Dark, "#2563EB");

#if (EnableNotifications)
        var notify = host.Services.GetRequiredService<INotificationService>();
        var res = await notify.PublishAsync(new AppNotificationRequest("Template Check", "Verification notification", NotificationSeverity.Info));
        if (!res.Persisted)
            return 2;
#endif
#if (EnableDiagnostics)
        var diag = host.Services.GetRequiredService<IDiagnosticsService>();
        var metrics = diag.CaptureMetrics();
        if (metrics.WorkingSetBytes <= 0)
            return 3;
#endif
        await host.StopAsync();
        Console.WriteLine($"TEMPLATE_SELF_TEST_PASSED nav={navOk} dark={theme.IsDarkEffective}");
        return navOk ? 0 : 1;
    }
}
