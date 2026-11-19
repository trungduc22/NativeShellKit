using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Abstractions.Threading;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.Diagnostics.Verification;
using NativeShellKit.Tray;
using NativeShellKit.Windows.Startup;
using NativeShellKit.Windows.SystemServices;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;
using Windows.Graphics;

namespace BackgroundCompanionApp;

public sealed partial class MainWindow : Window
{
    private readonly IAppHost _host;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly INotificationService _notifications;
    private readonly INativeNotificationPresenter _presenter;
    private readonly NativeTrayService _tray;
    private readonly WindowsStartupService _startup;
    private readonly IShellWindowService _windowService;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDisposable _shellAttachment;
    private readonly string? _interactiveOutputDir;
    private readonly Action<int>? _onAcceptanceComplete;
    private bool _acceptanceStarted;
    private int _bannersShownCount;

    public MainWindow(
        IAppHost host,
        string? interactiveOutputDir = null,
        Action<int>? onAcceptanceComplete = null)
    {
        _host = host;
        _interactiveOutputDir = interactiveOutputDir;
        _onAcceptanceComplete = onAcceptanceComplete;
        InitializeComponent();

        _navigation = _host.Services.GetRequiredService<INavigationService>();
        _theme = _host.Services.GetRequiredService<IThemeService>();
        _notifications = _host.Services.GetRequiredService<INotificationService>();
        _presenter = _host.Services.GetRequiredService<INativeNotificationPresenter>();
        _tray = _host.Services.GetRequiredService<NativeTrayService>();
        _startup = _host.Services.GetRequiredService<WindowsStartupService>();
        _windowService = _host.Services.GetRequiredService<IShellWindowService>();
        _dispatcher = _host.Services.GetRequiredService<IUiDispatcher>();

        _notifications.InAppNotificationRaised += OnBannerCountIncrement;

        RegisterRoutes();

        _shellAttachment = ShellFrame.AttachServices(
            _navigation,
            _theme,
            notificationService: _notifications,
            windowService: _windowService,
            uiDispatcher: _dispatcher,
            window: this,
            appTitle: _host.Identity.DisplayName);

        ShellFrame.TitleBarStatusContent = new StatusBadge
        {
            State = ShellStatusKind.Healthy,
            Label = "Tray Ready"
        };

        ConfigureTrayMenu(
            _tray,
            _notifications,
            _windowService,
            () => DispatcherQueue.TryEnqueue(Close),
            action => DispatcherQueue.TryEnqueue(() => action()));
        _tray.Initialize(_windowService.WindowHandle);
        Closed += (_, _) =>
        {
            _notifications.InAppNotificationRaised -= OnBannerCountIncrement;
            _shellAttachment.Dispose();
            _tray.Dispose();
        };

        if (!string.IsNullOrWhiteSpace(_interactiveOutputDir))
        {
            ShellFrame.Loaded += OnLoadedForAcceptanceAsync;
        }
    }

    private void OnBannerCountIncrement(object? sender, NotificationRecord record)
    {
        Interlocked.Increment(ref _bannersShownCount);
    }

    private async void OnLoadedForAcceptanceAsync(object sender, RoutedEventArgs e)
    {
        if (_acceptanceStarted)
            return;
        _acceptanceStarted = true;

        int exitCode = 1;
        try
        {
            exitCode = await RunInteractiveAcceptanceAsync(_interactiveOutputDir!);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BackgroundCompanionApp interactive acceptance error: {ex}");
            exitCode = 2;
        }
        finally
        {
            _tray.RequestFullExit();
            _onAcceptanceComplete?.Invoke(exitCode);
            Close();
        }
    }

    private async Task<int> RunInteractiveAcceptanceAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        await Task.Delay(350);

        nint hwnd = _windowService.WindowHandle;
        double dpiScale = new WindowManagerService().GetDpiScale(hwnd);
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.Resize(new SizeInt32(1100, 740));
        await Task.Delay(250);

        _theme.ApplyTheme(AppThemeMode.Dark, "#2563EB");
        _navigation.Navigate("status");
        await Task.Delay(350);
        string shot1 = Path.Combine(outputDir, "companion-1-dark-status.png");
        bool cap1 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot1);

        // Exercise System Tray menu callback + HideToTray/ShowFromTray recovery
        bool beforeQuiet = _notifications.Preferences.QuietHoursEnabled;
        _tray.MenuItems.First(m => m.Text == "Toggle Quiet Hours").Callback?.Invoke();
        bool toggledQuiet = _notifications.Preferences.QuietHoursEnabled != beforeQuiet;
        _notifications.Preferences.QuietHoursEnabled = false;

        bool restoreEventFired = false;
        _tray.RestoreRequested += (_, _) => restoreEventFired = true;
        _tray.HideToTray();
        await Task.Delay(150);
        _tray.ShowFromTray();
        await Task.Delay(200);

        // Switch to Light theme, publish notification, observe managed NativeShellFrame banner stack & activation
        _theme.ApplyTheme(AppThemeMode.Light, "#2563EB");
        bool handlerActivated = false;
        _notifications.RegisterActionHandler("open", _ =>
        {
            handlerActivated = true;
            return Task.CompletedTask;
        });

        var dispatchResult = await _notifications.PublishAsync(new AppNotificationRequest(
            "Background Companion Sync",
            "Desktop session active; click-to-open routes to Notifications.",
            NotificationSeverity.Success,
            ActivationDestination: "notifications",
            DeduplicationKey: $"interactive-{DateTimeOffset.UtcNow.Ticks}"));

        await Task.Delay(300);
        string activationArgs = _notifications.BuildActivationArguments(dispatchResult.NotificationId, "open", "notifications");
        bool activatedOk = await _notifications.TryHandleActivationAsync(activationArgs);
        await Task.Delay(350);

        string shot2 = Path.Combine(outputDir, "companion-2-light-banner-notifications.png");
        bool cap2 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot2);

        bool trayVerifiedOnSession = !_tray.HasTaskbarShell || _tray.IsIconActive;
        bool uiChecks = hwnd != nint.Zero &&
                        ShellFrame.ActualWidth > 0 &&
                        ShellFrame.ShellTitleBar.ActualWidth > 0 &&
                        trayVerifiedOnSession &&
                        toggledQuiet &&
                        restoreEventFired &&
                        _bannersShownCount >= 1 &&
                        activatedOk &&
                        handlerActivated &&
                        string.Equals(_navigation.CurrentRoute, "notifications", StringComparison.OrdinalIgnoreCase) &&
                        cap1 && cap2;

        var report = new
        {
            sample = "BackgroundCompanionApp",
            passed = uiChecks,
            hwnd = $"0x{hwnd:X}",
            dpiScale,
            micaSupported = _theme.IsMicaSupported,
            effectiveBackdrop = (_theme as WinUiThemeService)?.EffectiveBackdrop.ToString(),
            hasTaskbarShell = _tray.HasTaskbarShell,
            trayIconActive = _tray.IsIconActive,
            trayLastError = _tray.LastTrayError,
            trayMenuToggleWorked = toggledQuiet,
            trayRestoreEventFired = restoreEventFired,
            presenterRegistrationStatus = _presenter.RegistrationStatus.ToString(),
            presenterRegistrationError = _presenter.RegistrationError,
            nativeNotificationSubmissionStatus = dispatchResult.NativeSubmissionStatus.ToString(),
            nativeNotificationPresentedNatively = dispatchResult.PresentedNatively,
            nativeNotificationError = dispatchResult.NativeErrorMessage,
            visualDisplayConfirmed = dispatchResult.VisualDisplayConfirmed,
            inAppBannersShown = _bannersShownCount,
            clickToOpenActivated = activatedOk && handlerActivated,
            currentRouteAfterActivation = _navigation.CurrentRoute,
            screenshots = new[] { shot1, shot2 }
        };

        File.WriteAllText(
            Path.Combine(outputDir, "BackgroundCompanionApp-acceptance.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        if (uiChecks)
            Console.WriteLine($"COMPANION_APP_INTERACTIVE_ACCEPTANCE_PASSED taskbar={_tray.HasTaskbarShell} tray={_tray.IsIconActive} reg={_presenter.RegistrationStatus} native={dispatchResult.NativeSubmissionStatus}");

        return uiChecks ? 0 : 1;
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
        _navigation.RegisterRoute(new NavigationRouteDescriptor("status", "Companion", "\uE9D9", BuildStatusPage, Order: 0, CachePolicy: NavigationCachePolicy.Cached));
        _navigation.RegisterRoute(new NavigationRouteDescriptor("notifications", "Notifications", "\uEA8F", BuildNotificationsPage, Order: 1));
    }

    private object BuildStatusPage()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new PageHeader
        {
            Title = _host.Identity.DisplayName,
            Subtitle = "Single-instance tray companion with supervised heartbeat and startup integration",
            BadgeState = ShellStatusKind.Active,
            BadgeLabel = "Background"
        });

        stack.Children.Add(new StatusBadge
        {
            State = _tray.IsIconActive ? ShellStatusKind.Healthy : ShellStatusKind.Warning,
            Label = _tray.IsIconActive ? "System Tray Active" : "Tray Not Initialized",
            AnnounceChanges = true
        });

        stack.Children.Add(new ProgressBanner
        {
            Title = "Heartbeat Supervisor",
            StatusCaption = "Background worker loop healthy and awaiting activation events",
            ProgressPercent = 100
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
        var section = new SettingsSection { Title = "Notification Policy", Description = "Deduplication, history, and quiet hours" };
        var quietBtn = new Button { Content = "Toggle Quiet Hours" };
        quietBtn.Click += (_, _) => _notifications.Preferences.QuietHoursEnabled = !_notifications.Preferences.QuietHoursEnabled;
        section.AddRow(new SettingsRow
        {
            Header = "Quiet Hours",
            Description = "Suppress banners and native toasts during quiet schedule",
            Glyph = "\uE708",
            ActionContent = quietBtn
        });
        return new ScrollViewer { Content = section };
    }
}
