using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Abstractions.Threading;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.DesignSystem.Themes;
using NativeShellKit.Diagnostics.Verification;
using NativeShellKit.Windows.SystemServices;
using NativeShellKit.WinUI.Dialogs;
using NativeShellKit.WinUI.Mvvm;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;
using Windows.Graphics;

namespace MinimalNativeApp;

public sealed partial class MainWindow : Window
{
    private readonly IAppHost _host;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;
    private readonly IShellWindowService _windowService;
    private readonly DialogCoordinator _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDisposable _shellAttachment;
    private readonly string? _interactiveOutputDir;
    private readonly Action<int>? _onAcceptanceComplete;
    private bool _acceptanceStarted;

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
        _settings = _host.Services.GetRequiredService<ISettingsService>();
        _windowService = _host.Services.GetRequiredService<IShellWindowService>();
        _dialogs = _host.Services.GetRequiredService<DialogCoordinator>();
        _dispatcher = _host.Services.GetRequiredService<IUiDispatcher>();

        RegisterRoutes(_navigation, _host, _theme, _settings, _dialogs);

        ShellFrame.TitleBarSearchContent = new SearchBox
        {
            Placeholder = "Search controls or settings...",
            ShortcutHint = "Ctrl+K"
        };
        ShellFrame.TitleBarStatusContent = new StatusBadge
        {
            State = ShellStatusKind.Healthy,
            Label = "UI 2.0"
        };

        _shellAttachment = ShellFrame.AttachServices(
            _navigation,
            _theme,
            notificationService: null,
            windowService: _windowService,
            uiDispatcher: _dispatcher,
            window: this,
            appTitle: _host.Identity.DisplayName);

        ShellFrame.Loaded += OnShellLoaded;
        Closed += (_, _) => _shellAttachment.Dispose();
    }

    private void OnShellLoaded(object sender, RoutedEventArgs e)
    {
        if (ShellFrame.XamlRoot is not null)
            _dialogs.AttachXamlRoot(ShellFrame.XamlRoot);

        if (!string.IsNullOrWhiteSpace(_interactiveOutputDir) && !_acceptanceStarted)
        {
            _acceptanceStarted = true;
            _ = RunAndCloseAsync();
        }
    }

    private async Task RunAndCloseAsync()
    {
        int exitCode = 1;
        try
        {
            exitCode = await RunInteractiveAcceptanceAsync(_interactiveOutputDir!);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MinimalNativeApp interactive acceptance error: {ex}");
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
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new SizeInt32(1180, 780));
        await Task.Delay(250);
        var initialSize = appWindow.Size;

        var routesVisited = new List<string>();
        _theme.ApplyTheme(AppThemeMode.Dark, "#0F766E");
        bool darkOk = _theme.IsDarkEffective;
        _navigation.Navigate("overview");
        routesVisited.Add(_navigation.CurrentRoute ?? string.Empty);
        await Task.Delay(350);
        string shot1 = Path.Combine(outputDir, "minimal-1-dark-overview.png");
        bool cap1 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot1);

        _navigation.Navigate("controls");
        routesVisited.Add(_navigation.CurrentRoute ?? string.Empty);
        await Task.Delay(350);
        string shot2 = Path.Combine(outputDir, "minimal-2-dark-controls.png");
        bool cap2 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot2);

        // Verify CompactUtility + Compact density
        ShellFrame.Mode = ShellMode.CompactUtility;
        ShellFrame.Density = ShellDensity.Compact;
        DesignSystemThemeResources.ApplyDensity(ShellDensity.Compact);
        _navigation.Navigate("settings");
        routesVisited.Add(_navigation.CurrentRoute ?? string.Empty);
        await Task.Delay(350);
        bool compactModeOk = ShellFrame.UtilityNavigationBar.Visibility == Visibility.Visible;
        string shot3 = Path.Combine(outputDir, "minimal-3-dark-compact-utility.png");
        bool cap3 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot3);

        // Verify Dashboard + Light + Narrow breakpoint (LeftMinimal)
        ShellFrame.Mode = ShellMode.Dashboard;
        ShellFrame.Density = ShellDensity.Standard;
        DesignSystemThemeResources.ApplyDensity(ShellDensity.Standard);
        _theme.ApplyTheme(AppThemeMode.Light, "#0F766E");
        bool lightOk = !_theme.IsDarkEffective;
        appWindow.Resize(new SizeInt32(600, 720));
        _navigation.Navigate("overview");
        await Task.Delay(350);
        var resizedSize = appWindow.Size;
        bool narrowMinimalOk = ShellFrame.NavigationControl.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftMinimal;
        string shot4 = Path.Combine(outputDir, "minimal-4-light-narrow.png");
        bool cap4 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot4);

        var store = _settings.GetStore<MinimalAppPreferences>();
        await store.SaveAsync(new MinimalAppPreferences { Theme = AppThemeMode.Light, AccentHex = "#0F766E" });
        var loaded = await store.LoadAsync();

        bool uiChecks = hwnd != nint.Zero &&
                        ShellFrame.ActualWidth > 0 &&
                        ShellFrame.ShellTitleBar.ActualWidth > 0 &&
                        ShellFrame.ShellTitleBar.LastPassthroughRects.Count >= 1 &&
                        dpiScale > 0 &&
                        initialSize.Width >= 1000 &&
                        resizedSize.Width < initialSize.Width &&
                        darkOk && lightOk &&
                        compactModeOk && narrowMinimalOk &&
                        cap1 && cap2 && cap3 && cap4 &&
                        loaded.Theme == AppThemeMode.Light;

        var report = new
        {
            sample = "MinimalNativeApp",
            passed = uiChecks,
            hwnd = $"0x{hwnd:X}",
            dpiScale,
            micaSupported = _theme.IsMicaSupported,
            effectiveBackdrop = (_theme as WinUiThemeService)?.EffectiveBackdrop.ToString(),
            titleBarWidth = ShellFrame.ShellTitleBar.ActualWidth,
            passthroughRects = ShellFrame.ShellTitleBar.LastPassthroughRects.Count,
            compactModeOk,
            narrowMinimalOk,
            initialSize = new { initialSize.Width, initialSize.Height },
            resizedSize = new { resizedSize.Width, resizedSize.Height },
            routesVisited,
            screenshots = new[] { shot1, shot2, shot3, shot4 }
        };

        File.WriteAllText(
            Path.Combine(outputDir, "MinimalNativeApp-acceptance.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        if (uiChecks)
            Console.WriteLine($"MINIMAL_APP_INTERACTIVE_ACCEPTANCE_PASSED dpi={dpiScale} compact={compactModeOk} narrow={narrowMinimalOk}");

        return uiChecks ? 0 : 1;
    }

    internal static void RegisterRoutes(
        INavigationService navigation,
        IAppHost host,
        IThemeService theme,
        ISettingsService settings,
        IDialogService? dialogs = null)
    {
        navigation.RegisterRoute(new NavigationRouteDescriptor("overview", "Overview", "\uE80F", () => BuildOverviewPage(host, dialogs), Order: 0, CachePolicy: NavigationCachePolicy.Cached));
        navigation.RegisterRoute(new NavigationRouteDescriptor("controls", "Controls", "\uE74C", BuildControlsPage, Order: 1));
        navigation.RegisterRoute(new NavigationRouteDescriptor("settings", "Settings", "\uE713", () => BuildSettingsPage(theme, settings), Order: 10, IsFooterItem: true));
    }

    private static object BuildOverviewPage(IAppHost host, IDialogService? dialogs)
    {
        var stack = new StackPanel { Spacing = 16 };
        var confirmBtn = new Button { Content = "Confirm Action" };
        if (dialogs is not null)
        {
            confirmBtn.Click += async (_, _) =>
            {
                await dialogs.ShowDestructiveConfirmAsync(new DestructiveDialogRequest(
                    "Reset Showcase State?",
                    "Demonstrates destructive dialog safety with Close as default button.",
                    "Reset"));
            };
        }

        stack.Children.Add(new PageHeader
        {
            Title = host.Identity.DisplayName,
            Subtitle = "Fluent 2 NativeShellFrame host powered by NativeShellKit 0.2.0-alpha.4 NuGet packages",
            BadgeState = ShellStatusKind.Healthy,
            BadgeLabel = host.Identity.Version,
            ActionsContent = confirmBtn
        });

        var strip = new MetricStrip { WideColumns = 3, MediumColumns = 2 };
        strip.Children.Add(new MetricCard
        {
            Label = "Architecture",
            Value = "WinUI 3",
            Caption = "Native x64 XAML-First",
            Glyph = "\uE756",
            TrendState = ShellStatusKind.Healthy
        });
        strip.Children.Add(new MetricCard
        {
            Label = "Identity",
            Value = host.Identity.AppId,
            Caption = host.Identity.AppUserModelId,
            Glyph = "\uE8D7",
            TrendState = ShellStatusKind.Active
        });
        strip.Children.Add(new MetricCard
        {
            Label = "WCAG AA Accent",
            Value = "4.5:1+",
            Caption = "HighContrast-safe",
            Glyph = "\uE790",
            TrendState = ShellStatusKind.Healthy
        });
        stack.Children.Add(strip);

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        statusRow.Children.Add(new StatusBadge { State = ShellStatusKind.Healthy, Label = "Host Active" });
        statusRow.Children.Add(new StatusBadge { State = ShellStatusKind.Syncing, Label = "Tokens Synced" });
        statusRow.Children.Add(new StatusBadge { State = ShellStatusKind.Warning, Label = "Preview Build" });
        stack.Children.Add(statusRow);

        stack.Children.Add(new ProgressBanner
        {
            Title = "Fluent 2 Design Token Pipeline",
            StatusCaption = "All semantic brushes & templated controls loaded from compiled .xbf/.pri",
            ProgressPercent = 100
        });

        return new ScrollViewer { Content = stack };
    }

    private static object BuildControlsPage()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new PageHeader
        {
            Title = "Design System 2.0 Showcase",
            Subtitle = "Templated controls with 100% DependencyProperty & VisualState backing",
            BadgeState = ShellStatusKind.Active,
            BadgeLabel = "0.2.0-alpha.4"
        });

        stack.Children.Add(new SearchBox { Placeholder = "Filter components...", ShortcutHint = "Ctrl+K" });
        stack.Children.Add(new InlineProgressIndicator { IsActive = true, Caption = "Live telemetry channel ready" });

        var stateView = new StateView
        {
            PrimaryActionLabel = "Cycle Preview",
            SecondaryActionLabel = "Docs"
        };
        stateView.SetState(
            ShellViewState.Degraded,
            "Module Degraded Preview",
            "Demonstrates partial availability without blocking the shell window.");
        stateView.PrimaryActionCommand = new RelayCommand(() =>
        {
            stateView.State = stateView.State == ShellViewState.Degraded
                ? ShellViewState.Skeleton
                : ShellViewState.Degraded;
        });
        stack.Children.Add(stateView);

        return new ScrollViewer { Content = stack };
    }

    private static object BuildSettingsPage(IThemeService theme, ISettingsService settings)
    {
        var section = new SettingsSection
        {
            Title = "Appearance & Persistence",
            Description = "Light, Dark, System theming saved cleanly to settings.json"
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        foreach (var mode in new[] { AppThemeMode.Light, AppThemeMode.Dark, AppThemeMode.System })
        {
            var btn = new Button { Content = mode.ToString() };
            btn.Click += async (_, _) =>
            {
                theme.ApplyTheme(mode, "#0F766E");
                var store = settings.GetStore<MinimalAppPreferences>();
                await store.SaveAsync(new MinimalAppPreferences { Theme = mode, AccentHex = "#0F766E" });
            };
            panel.Children.Add(btn);
        }

        section.AddRow(new SettingsRow
        {
            Header = "Application Theme",
            Description = "Switch mode and persist settings under the isolated AppIdentity folder",
            Glyph = "\uE793",
            IsExpandable = true,
            IsExpanded = true,
            ActionContent = panel,
            ExpandedContent = new TextBlock
            {
                Text = "Backdrop automatically respects Windows High Contrast and falls back cleanly on Win10.",
                TextWrapping = TextWrapping.Wrap
            }
        });

        return new ScrollViewer { Content = section };
    }
}
