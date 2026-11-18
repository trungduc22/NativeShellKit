using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
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

    public MainWindow(IAppHost host)
    {
        _host = host;
        InitializeComponent();

        _navigation = _host.Services.GetRequiredService<INavigationService>();
        _theme = _host.Services.GetRequiredService<IThemeService>();
        var windowService = _host.Services.GetRequiredService<IShellWindowService>();

        if (windowService is ShellWindowService winUiWindow)
            winUiWindow.AttachWindow(this, ShellView.ShellTitleBar);
        if (_theme is WinUiThemeService winUiTheme)
            winUiTheme.TryApplyBackdrop(this);

        RegisterRoutes();
        ShellView.Initialize(_host.Identity.DisplayName, _navigation, _theme);

#if (EnableNotifications)
        var notifications = _host.Services.GetRequiredService<INotificationService>();
        notifications.InAppNotificationRaised += (_, record) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var banner = new NotificationBannerControl();
                banner.Show(record);
                banner.Dismissed += (_, _) => ShellView.SetBannerContent(null);
                ShellView.SetBannerContent(banner);
            });
        };
#endif

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
        _navigation.RegisterRoute(new NavigationRouteDescriptor("overview", "Overview", "\uE80F", BuildOverviewPage, Order: 0));
#if (EnableNavigation)
        _navigation.RegisterRoute(new NavigationRouteDescriptor("settings", "Settings", "\uE713", BuildSettingsPage, Order: 10, IsFooterItem: true));
#endif
    }

    private object BuildOverviewPage()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new PageHeaderControl
        {
            Title = _host.Identity.DisplayName,
            Subtitle = "Built from NativeShellKit.Templates",
            BadgeText = _host.Identity.Version
        });

        var card = new CompactMetricCard
        {
            Label = "SDK Status",
            Value = "Ready",
            Caption = _host.Identity.AppUserModelId,
            Glyph = "\uE756"
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
        var section = new SettingsSectionControl { Title = "Appearance", Description = "Theme & system integration" };
        var toggle = new Button { Content = "Toggle Light / Dark" };
        toggle.Click += (_, _) => _theme.ApplyTheme(_theme.IsDarkEffective ? AppThemeMode.Light : AppThemeMode.Dark);
        section.AddRow(new SettingsRowControl
        {
            Header = "Application Theme",
            Description = "Switch between light and dark fluent themes",
            Glyph = "\uE793",
            ActionContent = toggle
        });
        return new ScrollViewer { Content = section };
    }
}
