using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.Tray;
using NativeShellKit.Windows.Startup;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;

namespace BackgroundCompanionApp;

public sealed partial class MainWindow : Window
{
    private readonly IAppHost _host;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly INotificationService _notifications;
    private readonly NativeTrayService _tray;
    private readonly WindowsStartupService _startup;

    public MainWindow(IAppHost host)
    {
        _host = host;
        InitializeComponent();

        _navigation = _host.Services.GetRequiredService<INavigationService>();
        _theme = _host.Services.GetRequiredService<IThemeService>();
        _notifications = _host.Services.GetRequiredService<INotificationService>();
        _tray = _host.Services.GetRequiredService<NativeTrayService>();
        _startup = _host.Services.GetRequiredService<WindowsStartupService>();
        var windowService = _host.Services.GetRequiredService<IShellWindowService>();

        if (windowService is ShellWindowService winUiWindow)
            winUiWindow.AttachWindow(this, ShellView.ShellTitleBar);
        if (_theme is WinUiThemeService winUiTheme)
            winUiTheme.TryApplyBackdrop(this);

        ConfigureTrayMenu(_tray, _notifications, windowService, () => DispatcherQueue.TryEnqueue(Close), action => DispatcherQueue.TryEnqueue(() => action()));
        _tray.Initialize(windowService.WindowHandle);
        Closed += (_, _) => _tray.Dispose();

        _notifications.InAppNotificationRaised += (_, record) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var banner = new NotificationBannerControl();
                banner.Show(record);
                banner.Dismissed += (_, _) => ShellView.SetBannerContent(null);
                ShellView.SetBannerContent(banner);
            });
        };

        RegisterRoutes();
        ShellView.Initialize(_host.Identity.DisplayName, _navigation, _theme);
    }

    internal static void ConfigureTrayMenu(
        NativeTrayService tray,
        INotificationService notifications,
        IShellWindowService? windowService,
        Action onExit,
        Action<Action>? dispatchToUi = null)
    {
        void RunOnUi(Action action)
        {
            if (dispatchToUi is not null)
                dispatchToUi(action);
            else
                action();
        }

        tray.SetMenuItems(
        [
            new TrayMenuItem(1, "Open", () => RunOnUi(() => windowService?.ShowAndActivate())),
            new TrayMenuItem(2, "Send Test Notification", () =>
                _ = notifications.PublishAsync(new AppNotificationRequest(
                    "Companion Alert",
                    "Triggered from tray menu.",
                    NotificationSeverity.Info,
                    DeduplicationKey: $"manual-{DateTimeOffset.UtcNow.Ticks}"))),
            new TrayMenuItem(3, "Toggle Quiet Hours", () =>
            {
                notifications.Preferences.QuietHoursEnabled = !notifications.Preferences.QuietHoursEnabled;
            }),
            new TrayMenuItem(4, "Separator", IsSeparator: true),
            new TrayMenuItem(5, "Exit", () => RunOnUi(() =>
            {
                tray.RequestFullExit();
                onExit();
            }))
        ]);
    }

    private void RegisterRoutes()
    {
        _navigation.RegisterRoute(new NavigationRouteDescriptor("status", "Companion", "\uE9D9", BuildStatusPage, Order: 0));
        _navigation.RegisterRoute(new NavigationRouteDescriptor("notifications", "Notifications", "\uEA8F", BuildNotificationsPage, Order: 1));
    }

    private object BuildStatusPage()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new PageHeaderControl
        {
            Title = _host.Identity.DisplayName,
            Subtitle = "Single-instance tray companion with supervised heartbeat and startup integration",
            BadgeText = "Background"
        });

        var autoStartBtn = new Button
        {
            Content = _startup.IsEnabled() ? "Disable Auto-Start" : "Enable Auto-Start"
        };
        autoStartBtn.Click += (_, _) =>
        {
            if (_startup.IsEnabled())
                _startup.Disable();
            else if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                _startup.Enable(Environment.ProcessPath, startMinimized: true);
            autoStartBtn.Content = _startup.IsEnabled() ? "Disable Auto-Start" : "Enable Auto-Start";
        };
        stack.Children.Add(autoStartBtn);

        return new ScrollViewer { Content = stack };
    }

    private object BuildNotificationsPage()
    {
        var section = new SettingsSectionControl { Title = "Notification Policy", Description = "Deduplication, history, and quiet hours" };
        var quietBtn = new Button { Content = "Toggle Quiet Hours" };
        quietBtn.Click += (_, _) => _notifications.Preferences.QuietHoursEnabled = !_notifications.Preferences.QuietHoursEnabled;
        section.AddRow(new SettingsRowControl
        {
            Header = "Quiet Hours",
            Description = "Suppress banners and native toasts during quiet schedule",
            Glyph = "\uE708",
            ActionContent = quietBtn
        });
        return new ScrollViewer { Content = section };
    }
}
