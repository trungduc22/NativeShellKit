using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.Diagnostics.Verification;
using NativeShellKit.Windows.SystemServices;
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
    private readonly string? _interactiveOutputDir;
    private readonly Action<int>? _onAcceptanceComplete;
    private bool _backdropApplied;
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
        _catalog = _host.Services.GetRequiredService<IModuleCatalog>();
        _notifications = _host.Services.GetRequiredService<INotificationService>();
        _presenter = _host.Services.GetRequiredService<INativeNotificationPresenter>();
        _windowService = _host.Services.GetRequiredService<IShellWindowService>();

        if (_windowService is ShellWindowService winUiWindow)
            winUiWindow.AttachWindow(this, ShellView.ShellTitleBar);
        if (_theme is WinUiThemeService winUiTheme)
            _backdropApplied = winUiTheme.TryApplyBackdrop(this);

        _notifications.InAppNotificationRaised += (_, record) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _bannersShownCount++;
                var banner = new NotificationBannerControl();
                banner.Show(record);
                banner.Dismissed += (_, _) => ShellView.SetBannerContent(null);
                ShellView.SetBannerContent(banner);
            });
        };

        RegisterModuleRoutes();
        ShellView.Initialize(_host.Identity.DisplayName, _navigation, _theme);

        if (!string.IsNullOrWhiteSpace(_interactiveOutputDir))
        {
            ShellView.Loaded += OnLoadedForAcceptanceAsync;
        }
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
        await Task.Delay(200);

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
                        ShellView.ActualWidth > 0 &&
                        ShellView.ShellTitleBar.ActualWidth > 0 &&
                        dpiScale > 0 &&
                        cmdExecuted &&
                        sessionState.CompletedSnapshots == 1 &&
                        archiveState.LastIndexedCount == 42 &&
                        _bannersShownCount >= 1 &&
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
            backdropApplied = _backdropApplied,
            modulesLoaded = _catalog.Modules.Count,
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
                _ => () => new PageHeaderControl { Title = item.Title, Subtitle = item.ModuleId }
            };
            _navigation.RegisterRoute(new NavigationRouteDescriptor(item.Route, item.Title, item.Glyph, viewFactory, item.ModuleId, item.Order, item.IsFooterItem));
        }
    }

    private object BuildSessionsPage()
    {
        var state = _host.Services.GetRequiredService<WorkspaceSessionState>();
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new PageHeaderControl
        {
            Title = "Workspace Session Module",
            Subtitle = "Emits WorkspaceSnapshotRequestedEvent across the in-process event bus",
            BadgeText = "Module 1"
        });
        stack.Children.Add(new CompactMetricCard
        {
            Label = "Completed Snapshots",
            Value = state.CompletedSnapshots.ToString(),
            Caption = state.LastCompletedWorkspace ?? "None yet",
            Glyph = "\uE7F4"
        });
        var trigger = new Button { Content = "Snapshot Workspace" };
        trigger.Click += async (_, _) => await _catalog.ExecuteCommandAsync("sessions.snapshot", _host.Services);
        stack.Children.Add(trigger);
        return new ScrollViewer { Content = stack };
    }

    private object BuildArchivePage()
    {
        var state = _host.Services.GetRequiredService<ArchiveState>();
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new PageHeaderControl
        {
            Title = "Code Archive Companion Module",
            Subtitle = "Handles snapshot requests independently and notifies user via INotificationService",
            BadgeText = "Module 2"
        });
        stack.Children.Add(new CompactMetricCard
        {
            Label = "Indexed Items",
            Value = state.LastIndexedCount.ToString(),
            Caption = state.LastWorkspace ?? "Idle",
            Glyph = "\uE8B7"
        });
        return new ScrollViewer { Content = stack };
    }
}
