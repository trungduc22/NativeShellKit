using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Modules;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.WinUI.Shell;
using NativeShellKit.WinUI.Theming;
using UnifiedHostDemo.Modules;

namespace UnifiedHostDemo;

public sealed partial class MainWindow : Window
{
    private readonly IAppHost _host;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IModuleCatalog _catalog;

    public MainWindow(IAppHost host)
    {
        _host = host;
        InitializeComponent();

        _navigation = _host.Services.GetRequiredService<INavigationService>();
        _theme = _host.Services.GetRequiredService<IThemeService>();
        _catalog = _host.Services.GetRequiredService<IModuleCatalog>();

        if (_host.Services.GetRequiredService<IShellWindowService>() is ShellWindowService winUiWindow)
            winUiWindow.AttachWindow(this, ShellView.ShellTitleBar);
        if (_theme is WinUiThemeService winUiTheme)
            winUiTheme.TryApplyBackdrop(this);

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

        RegisterModuleRoutes();
        ShellView.Initialize(_host.Identity.DisplayName, _navigation, _theme);
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
