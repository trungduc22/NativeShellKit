using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;

namespace MinimalNativeApp;

public sealed partial class MainWindow : Window
{
    private readonly IAppHost _host;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;

    public MainWindow(IAppHost host)
    {
        _host = host;
        InitializeComponent();

        _navigation = _host.Services.GetRequiredService<INavigationService>();
        _theme = _host.Services.GetRequiredService<IThemeService>();
        _settings = _host.Services.GetRequiredService<ISettingsService>();

        if (_host.Services.GetRequiredService<IShellWindowService>() is ShellWindowService winUiWindow)
            winUiWindow.AttachWindow(this, ShellView.ShellTitleBar);
        if (_theme is WinUiThemeService winUiTheme)
            winUiTheme.TryApplyBackdrop(this);

        RegisterRoutes(_navigation, _host, _theme, _settings);
        ShellView.Initialize(_host.Identity.DisplayName, _navigation, _theme);
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

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new CompactMetricCard { Label = "Architecture", Value = "WinUI 3 + XAML", Caption = "Native x64", Glyph = "\uE756" });
        row.Children.Add(new CompactMetricCard { Label = "Identity", Value = host.Identity.AppId, Caption = host.Identity.AppUserModelId, Glyph = "\uE8D7" });
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
            BadgeText = "0.1.0-alpha.1"
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
