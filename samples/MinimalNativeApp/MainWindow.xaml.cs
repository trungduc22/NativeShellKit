using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.Diagnostics.Verification;
using NativeShellKit.Windows.SystemServices;
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
    private readonly string? _interactiveOutputDir;
    private readonly Action<int>? _onAcceptanceComplete;
    private bool _backdropApplied;
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

        if (_windowService is ShellWindowService winUiWindow)
            winUiWindow.AttachWindow(this, ShellView.ShellTitleBar);
        if (_theme is WinUiThemeService winUiTheme)
            _backdropApplied = winUiTheme.TryApplyBackdrop(this);

        RegisterRoutes(_navigation, _host, _theme, _settings);
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

        appWindow.Resize(new SizeInt32(1140, 760));
        await Task.Delay(200);
        var initialSize = appWindow.Size;

        appWindow.Resize(new SizeInt32(980, 680));
        await Task.Delay(250);
        var resizedSize = appWindow.Size;

        var routesVisited = new List<string>();
        _theme.ApplyTheme(AppThemeMode.Dark, "#0F766E");
        bool darkOk = _theme.IsDarkEffective;
        _navigation.Navigate("overview");
        routesVisited.Add(_navigation.CurrentRoute ?? string.Empty);
        await Task.Delay(300);
        string shot1 = Path.Combine(outputDir, "minimal-1-dark-overview.png");
        bool cap1 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot1);

        _navigation.Navigate("controls");
        routesVisited.Add(_navigation.CurrentRoute ?? string.Empty);
        await Task.Delay(300);
        string shot2 = Path.Combine(outputDir, "minimal-2-dark-controls.png");
        bool cap2 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot2);

        _theme.ApplyTheme(AppThemeMode.Light, "#0F766E");
        bool lightOk = !_theme.IsDarkEffective;
        _navigation.Navigate("settings");
        routesVisited.Add(_navigation.CurrentRoute ?? string.Empty);
        await Task.Delay(300);
        string shot3 = Path.Combine(outputDir, "minimal-3-light-settings.png");
        bool cap3 = WindowScreenshotCapture.TryCaptureWindowToPng(hwnd, shot3);

        var store = _settings.GetStore<MinimalAppPreferences>();
        await store.SaveAsync(new MinimalAppPreferences { Theme = AppThemeMode.Light, AccentHex = "#0F766E" });
        var loaded = await store.LoadAsync();

        bool uiChecks = hwnd != nint.Zero &&
                        ShellView.ActualWidth > 0 &&
                        ShellView.ShellTitleBar.ActualWidth > 0 &&
                        ShellView.NavigationControl.ActualWidth > 0 &&
                        dpiScale > 0 &&
                        initialSize.Width >= 1000 &&
                        resizedSize.Width < initialSize.Width &&
                        darkOk && lightOk &&
                        cap1 && cap2 && cap3 &&
                        loaded.Theme == AppThemeMode.Light;

        var report = new
        {
            sample = "MinimalNativeApp",
            passed = uiChecks,
            hwnd = $"0x{hwnd:X}",
            dpiScale,
            micaSupported = _theme.IsMicaSupported,
            backdropApplied = _backdropApplied,
            titleBarWidth = ShellView.ShellTitleBar.ActualWidth,
            navigationViewWidth = ShellView.NavigationControl.ActualWidth,
            initialSize = new { initialSize.Width, initialSize.Height },
            resizedSize = new { resizedSize.Width, resizedSize.Height },
            routesVisited,
            screenshots = new[] { shot1, shot2, shot3 }
        };

        File.WriteAllText(
            Path.Combine(outputDir, "MinimalNativeApp-acceptance.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        if (uiChecks)
            Console.WriteLine($"MINIMAL_APP_INTERACTIVE_ACCEPTANCE_PASSED dpi={dpiScale} mica={_theme.IsMicaSupported} backdrop={_backdropApplied}");

        return uiChecks ? 0 : 1;
    }

    internal static void RegisterRoutes(
        INavigationService navigation,
        IAppHost host,
        IThemeService theme,
        ISettingsService settings)
    {
        navigation.RegisterRoute(new NavigationRouteDescriptor("overview", "Overview", "\uE80F", () => BuildOverviewPage(host), Order: 0));
        navigation.RegisterRoute(new NavigationRouteDescriptor("controls", "Controls", "\uE74C", BuildControlsPage, Order: 1));
        navigation.RegisterRoute(new NavigationRouteDescriptor("settings", "Settings", "\uE713", () => BuildSettingsPage(theme, settings), Order: 10, IsFooterItem: true));
    }

    private static object BuildOverviewPage(IAppHost host)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new PageHeaderControl
        {
            Title = host.Identity.DisplayName,
            Subtitle = "Reference minimal native WinUI 3 host powered by NativeShellKit NuGet packages",
            BadgeText = host.Identity.Version
        });

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var archCard = new CompactMetricCard { Label = "Architecture", Value = "WinUI 3 + XAML", Caption = "Native x64", Glyph = "\uE756" };
        var idCard = new CompactMetricCard { Label = "Identity", Value = host.Identity.AppId, Caption = host.Identity.AppUserModelId, Glyph = "\uE8D7" };
        Grid.SetColumn(archCard, 0);
        Grid.SetColumn(idCard, 1);
        row.Children.Add(archCard);
        row.Children.Add(idCard);
        stack.Children.Add(row);

        stack.Children.Add(new StatusIndicatorControl
        {
            State = ShellIndicatorState.Healthy,
            Label = $"SDK Host Active ({host.Identity.AppId})"
        });

        return new ScrollViewer { Content = stack };
    }

    private static object BuildControlsPage()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new PageHeaderControl
        {
            Title = "Design System Showcase",
            Subtitle = "Reusable WinUI 3 controls with Fluent tokens",
            BadgeText = "0.1.0-alpha.2"
        });

        stack.Children.Add(new SearchBarControl { Placeholder = "Filter controls or settings..." });
        var stateView = new StateViewControl();
        stateView.SetState(
            ShellViewState.Empty,
            "All Shared Controls Loaded",
            "PageHeaderControl, CompactMetricCard, SearchBarControl, StateViewControl, and SettingsSectionControl are loaded from compiled .xbf/.pri packages.");
        stack.Children.Add(stateView);

        return new ScrollViewer { Content = stack };
    }

    private static object BuildSettingsPage(IThemeService theme, ISettingsService settings)
    {
        var section = new SettingsSectionControl { Title = "Appearance & Persistence", Description = "Light, Dark, System theming saved cleanly to settings.json" };
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

        section.AddRow(new SettingsRowControl
        {
            Header = "Application Theme",
            Description = "Switch mode and persist settings under the isolated AppIdentity folder",
            Glyph = "\uE793",
            ActionContent = panel
        });

        return new ScrollViewer { Content = section };
    }
}
