using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Abstractions.Threading;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.Diagnostics.Verification;
using NativeShellKit.Windows.SystemServices;
using NativeShellKit.WinUI.Mvvm;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;
using UnifiedHostDemo.Modules;
using Windows.Graphics;

namespace UnifiedHostDemo;

public sealed partial class MainWindow : Window
{
    private readonly IAppHost _host;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IModuleCatalog _catalog;
    private readonly INotificationService _notifications;
    private readonly INativeNotificationPresenter _presenter;
    private readonly IShellWindowService _windowService;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDisposable _shellAttachment;
    private readonly string? _interactiveOutputDir;
    private readonly Action<int>? _onAcceptanceComplete;
    private bool _acceptanceStarted;
    private int _bannersShownCount;
    private int _navAwareHits;

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
        _catalog = _host.Services.GetRequiredService<IModuleCatalog>();
        _notifications = _host.Services.GetRequiredService<INotificationService>();
        _presenter = _host.Services.GetRequiredService<INativeNotificationPresenter>();
        _windowService = _host.Services.GetRequiredService<IShellWindowService>();
        _dispatcher = _host.Services.GetRequiredService<IUiDispatcher>();

        _notifications.InAppNotificationRaised += OnBannerCountIncrement;

        RegisterModuleRoutes();

        var mediaFooter = new CompactMediaBar
        {
            LayoutMode = MediaBarLayout.Strip,
            Title = "Focus Ambient — Live Workspace Stream",
            Artist = "Module Transport",
            ProviderLabel = "Host",
            RouteLabel = "PC",
            ConnectionState = ShellStatusKind.Healthy,
            IsPlaying = true,
            Margin = new Thickness(16, 0, 16, 12)
        };
        mediaFooter.PlayPauseCommand = new RelayCommand(() => mediaFooter.IsPlaying = !mediaFooter.IsPlaying);

        ShellFrame.FooterStripContent = mediaFooter;
        ShellFrame.TitleBarStatusContent = new StatusBadge
        {
            State = ShellStatusKind.Active,
            Label = $"{_catalog.Modules.Count} Modules"
        };

        _shellAttachment = ShellFrame.AttachServices(
            _navigation,
            _theme,
            notificationService: _notifications,
            windowService: _windowService,
            uiDispatcher: _dispatcher,
            window: this,
            appTitle: _host.Identity.DisplayName);

        Closed += (_, _) =>
        {
            _notifications.InAppNotificationRaised -= OnBannerCountIncrement;
            _shellAttachment.Dispose();
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
            Console.Error.WriteLine($"UnifiedHostDemo interactive acceptance error: {ex}");
            exitCode = 2;
        }
        finally
        {
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
        appWindow.Resize(new SizeInt32(1140, 760));
        await Task.Delay(250);

        _theme.ApplyTheme(AppThemeMode.Dark, "#2563EB");
        bool cmdExecuted = await _catalog.ExecuteCommandAsync("sessions.snapshot", _host.Services);
        _navigation.Navigate("sessions");
        await Task.Delay(350);
        string shot1 = Path.Combine(outputDir, "unified-1-dark-sessions.png");
        bool cap1 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot1);

        _theme.ApplyTheme(AppThemeMode.Light, "#2563EB");
        var history = await _notifications.GetHistoryAsync(5);
        bool activatedOk = history.Count > 0 &&
                           await _notifications.TryHandleActivationAsync(
                               _notifications.BuildActivationArguments(history[0].NotificationId, "open", "archive"));

        await Task.Delay(350);
        string shot2 = Path.Combine(outputDir, "unified-2-light-archive-banner.png");
        bool cap2 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot2);

        var sessionState = _host.Services.GetRequiredService<WorkspaceSessionState>();
        var archiveState = _host.Services.GetRequiredService<ArchiveState>();

        bool uiChecks = hwnd != nint.Zero &&
                        ShellFrame.ActualWidth > 0 &&
                        ShellFrame.ShellTitleBar.ActualWidth > 0 &&
                        dpiScale > 0 &&
                        cmdExecuted &&
                        sessionState.CompletedSnapshots == 1 &&
                        archiveState.LastIndexedCount == 42 &&
                        _bannersShownCount >= 1 &&
                        _navAwareHits >= 1 &&
                        activatedOk &&
                        string.Equals(_navigation.CurrentRoute, "archive", StringComparison.OrdinalIgnoreCase) &&
                        cap1 && cap2;

        var report = new
        {
            sample = "UnifiedHostDemo",
            passed = uiChecks,
            hwnd = $"0x{hwnd:X}",
            dpiScale,
            micaSupported = _theme.IsMicaSupported,
            effectiveBackdrop = (_theme as WinUiThemeService)?.EffectiveBackdrop.ToString(),
            modulesLoaded = _catalog.Modules.Count,
            navAwareHits = _navAwareHits,
            completedSnapshots = sessionState.CompletedSnapshots,
            indexedCount = archiveState.LastIndexedCount,
            presenterRegistrationStatus = _presenter.RegistrationStatus.ToString(),
            inAppBannersShown = _bannersShownCount,
            clickToOpenRoute = _navigation.CurrentRoute,
            screenshots = new[] { shot1, shot2 }
        };

        File.WriteAllText(
            Path.Combine(outputDir, "UnifiedHostDemo-acceptance.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        if (uiChecks)
            Console.WriteLine($"UNIFIED_HOST_DEMO_INTERACTIVE_ACCEPTANCE_PASSED modules={_catalog.Modules.Count} snapshots={sessionState.CompletedSnapshots}");

        return uiChecks ? 0 : 1;
    }

    private void RegisterModuleRoutes()
    {
        foreach (var item in _catalog.GetAllNavigationItems())
        {
            Func<object> viewFactory = item.Route switch
            {
                "sessions" => BuildSessionsPage,
                "archive" => BuildArchivePage,
                _ => () => new PageHeader { Title = item.Title, Subtitle = item.ModuleId }
            };
            _navigation.RegisterRoute(new NavigationRouteDescriptor(
                item.Route,
                item.Title,
                item.Glyph,
                viewFactory,
                item.ModuleId,
                item.Order,
                item.IsFooterItem,
                NavigationCachePolicy.Cached));
        }
    }

    private object BuildSessionsPage()
    {
        return new ModuleViewPage(this, BuildSessionsContent());
    }

    private UIElement BuildSessionsContent()
    {
        var state = _host.Services.GetRequiredService<WorkspaceSessionState>();
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new PageHeader
        {
            Title = "Workspace Session Module",
            Subtitle = "Emits WorkspaceSnapshotRequestedEvent across the in-process event bus",
            BadgeState = ShellStatusKind.Healthy,
            BadgeLabel = "Module 1"
        });

        var strip = new MetricStrip { WideColumns = 2, MediumColumns = 2 };
        strip.Children.Add(new MetricCard
        {
            Label = "Completed Snapshots",
            Value = state.CompletedSnapshots.ToString(),
            Caption = state.LastCompletedWorkspace ?? "None yet",
            Glyph = "\uE7F4",
            TrendState = ShellStatusKind.Healthy
        });
        strip.Children.Add(new MetricCard
        {
            Label = "Module Cache Policy",
            Value = "Cached",
            Caption = "INavigationAware enabled",
            Glyph = "\uE8B7",
            TrendState = ShellStatusKind.Active
        });
        stack.Children.Add(strip);

        var trigger = new Button { Content = "Snapshot Workspace" };
        trigger.Click += async (_, _) => await _catalog.ExecuteCommandAsync("sessions.snapshot", _host.Services);
        stack.Children.Add(trigger);

        var boundary = new ModuleCardBoundary
        {
            ModuleId = "workspace-session",
            ModuleTitle = "Workspace Session Health Boundary",
            HealthState = ModuleHealthState.Healthy,
            RetryLabel = "Recover",
            ModuleContent = new CompactMediaBar
            {
                LayoutMode = MediaBarLayout.Card,
                Title = "Workspace Audio Preview",
                Artist = "Local Session",
                ProviderLabel = "UnifiedHost",
                RouteLabel = "Connected",
                ConnectionState = ShellStatusKind.Healthy,
                PositionSeconds = 42,
                DurationSeconds = 180
            }
        };
        stack.Children.Add(boundary);

        return new ScrollViewer { Content = stack };
    }

    private object BuildArchivePage()
    {
        var state = _host.Services.GetRequiredService<ArchiveState>();
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new PageHeader
        {
            Title = "Code Archive Companion Module",
            Subtitle = "Handles snapshot requests independently and notifies user via INotificationService",
            BadgeState = ShellStatusKind.Healthy,
            BadgeLabel = "Module 2"
        });
        stack.Children.Add(new MetricCard
        {
            Label = "Indexed Items",
            Value = state.LastIndexedCount.ToString(),
            Caption = state.LastWorkspace ?? "Idle",
            Glyph = "\uE8B7",
            TrendState = ShellStatusKind.Healthy
        });

        var degradedSample = new ModuleCardBoundary
        {
            ModuleId = "optional-remote-relay",
            ModuleTitle = "Optional Peer Relay",
            HealthState = ModuleHealthState.Degraded,
            DegradedTitle = "Optional Peer Relay Unavailable",
            StatusMessage = "Fallback isolated via ModuleCardBoundary without affecting shell navigation.",
            RetryLabel = "Retry"
        };
        degradedSample.RetryCommand = new RelayCommand(() => degradedSample.HealthState = ModuleHealthState.Healthy);
        stack.Children.Add(degradedSample);

        return new ModuleViewPage(this, new ScrollViewer { Content = stack });
    }

    private sealed class ModuleViewPage : UserControl, INavigationAware
    {
        private readonly MainWindow _owner;

        public ModuleViewPage(MainWindow owner, UIElement content)
        {
            _owner = owner;
            Content = content;
        }

        public void OnNavigatedTo(object? parameter)
        {
            Interlocked.Increment(ref _owner._navAwareHits);
        }

        public void OnNavigatedFrom()
        {
        }
    }
}
