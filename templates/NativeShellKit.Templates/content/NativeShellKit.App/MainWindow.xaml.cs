using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Threading;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;
#if (EnableNotifications)
using NativeShellKit.Abstractions.Notifications;
#endif
#if (EnableTray)
using NativeShellKit.Tray;
#endif

namespace NativeShellKitApp;

public sealed partial class MainWindow : Window
{
    private readonly IAppHost _host;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IDisposable _shellAttachment;

    public MainWindow(IAppHost host)
    {
        _host = host;
        InitializeComponent();

        _navigation = _host.Services.GetRequiredService<INavigationService>();
        _theme = _host.Services.GetRequiredService<IThemeService>();
        var windowService = _host.Services.GetRequiredService<IShellWindowService>();
        var dispatcher = _host.Services.GetService<IUiDispatcher>();
#if (EnableNotifications)
        var notifications = _host.Services.GetRequiredService<INotificationService>();
#else
        NativeShellKit.Abstractions.Notifications.INotificationService? notifications = null;
#endif

        RegisterRoutes();

        _shellAttachment = ShellFrame.AttachServices(
            _navigation,
            _theme,
            notificationService: notifications,
            windowService: windowService,
            uiDispatcher: dispatcher,
            window: this,
            appTitle: _host.Identity.DisplayName);

        Closed += (_, _) => _shellAttachment.Dispose();

#if (EnableTray)
        var tray = _host.Services.GetRequiredService<NativeTrayService>();
        tray.SetMenuItems(
        [
            new TrayMenuItem(1, "Show Window", () => DispatcherQueue.TryEnqueue(windowService.ShowAndActivate)),
            new TrayMenuItem(2, "Exit", () => DispatcherQueue.TryEnqueue(() =>
            {
                tray.RequestFullExit();
                Close();
            }))
        ]);
        tray.Initialize(windowService.WindowHandle);
        Closed += (_, _) => tray.Dispose();
#endif
    }

    private void RegisterRoutes()
    {
        _navigation.RegisterRoute(new NavigationRouteDescriptor("overview", "Overview", "\uE80F", BuildOverviewPage, Order: 0, CachePolicy: NavigationCachePolicy.Cached));
#if (EnableNavigation)
        _navigation.RegisterRoute(new NavigationRouteDescriptor("settings", "Settings", "\uE713", BuildSettingsPage, Order: 10, IsFooterItem: true));
#endif
    }

    private object BuildOverviewPage()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new PageHeader
        {
            Title = _host.Identity.DisplayName,
            Subtitle = "Built from NativeShellKit.Templates",
            BadgeState = ShellStatusKind.Healthy,
            BadgeLabel = _host.Identity.Version
        });

        var card = new MetricCard
        {
            Label = "SDK Status",
            Value = "Ready",
            Caption = _host.Identity.AppUserModelId,
            Glyph = "\uE756",
            TrendState = ShellStatusKind.Healthy
        };
        stack.Children.Add(card);

#if (EnableNotifications)
        var btn = new Button { Content = "Send Test Notification" };
        btn.Click += async (_, _) =>
        {
            var notify = _host.Services.GetRequiredService<INotificationService>();
            await notify.PublishAsync(new AppNotificationRequest("Hello from NativeShellKitApp", "Native and in-app notification test.", NotificationSeverity.Success));
        };
        stack.Children.Add(btn);
#endif
        return new ScrollViewer { Content = stack };
    }

    private object BuildSettingsPage()
    {
        var section = new SettingsSection { Title = "Appearance", Description = "Theme & system integration" };
        var toggle = new Button { Content = "Toggle Light / Dark" };
        toggle.Click += (_, _) => _theme.ApplyTheme(_theme.IsDarkEffective ? AppThemeMode.Light : AppThemeMode.Dark);
        section.AddRow(new SettingsRow
        {
            Header = "Application Theme",
            Description = "Switch between light and dark fluent themes",
            Glyph = "\uE793",
            ActionContent = toggle
        });
        return new ScrollViewer { Content = section };
    }
}
