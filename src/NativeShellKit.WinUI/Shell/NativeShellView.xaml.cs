using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.WinUI.Navigation;

namespace NativeShellKit.WinUI.Shell;

public sealed partial class NativeShellView : UserControl, INotifyPropertyChanged
{
    private INavigationService? _navigationService;
    private IThemeService? _themeService;
    private bool _syncingSelection;
    private bool _canGoBack;

    public NativeShellView()
    {
        InitializeComponent();
        TitleBar.ThemeToggleRequested += OnThemeToggleRequested;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CustomTitleBarControl ShellTitleBar => TitleBar;
    public NavigationView NavigationControl => ShellNavigationView;

    public bool CanGoBack
    {
        get => _canGoBack;
        private set
        {
            if (_canGoBack != value)
            {
                _canGoBack = value;
                OnPropertyChanged();
            }
        }
    }

    public void Initialize(
        string appTitle,
        INavigationService navigationService,
        IThemeService themeService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

        TitleBar.TitleText = appTitle;
        if (themeService is Theming.WinUiThemeService winUiTheme)
        {
            winUiTheme.AttachRoot(this);
        }

        _navigationService.Navigated += OnNavigated;
        RebuildNavigationItems();

        if (_navigationService.CurrentRoute is null && _navigationService.RegisteredRoutes.Count > 0)
        {
            _navigationService.Navigate(_navigationService.RegisteredRoutes[0].Route);
        }
    }

    public void RebuildNavigationItems()
    {
        if (_navigationService is null)
            return;

        ShellNavigationView.MenuItems.Clear();
        ShellNavigationView.FooterMenuItems.Clear();

        foreach (var route in _navigationService.RegisteredRoutes)
        {
            var item = new NavigationViewItem
            {
                Content = route.Title,
                Tag = route.Route,
                Icon = new FontIcon { Glyph = string.IsNullOrWhiteSpace(route.Glyph) ? "\uE80F" : route.Glyph }
            };
            AutomationProperties.SetName(item, route.Title);

            if (route.IsFooterItem)
                ShellNavigationView.FooterMenuItems.Add(item);
            else
                ShellNavigationView.MenuItems.Add(item);
        }
    }

    public void SetBannerContent(object? content)
    {
        if (DispatcherQueue is { HasThreadAccess: false })
        {
            DispatcherQueue.TryEnqueue(() => SetBannerContent(content));
            return;
        }

        NotificationBannerHost.Content = content;
        NotificationBannerHost.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnNavigated(object? sender, Abstractions.Navigation.NavigationEventArgs e)
    {
        if (DispatcherQueue is { HasThreadAccess: false })
        {
            DispatcherQueue.TryEnqueue(() => ApplyNavigatedRoute(e));
            return;
        }

        ApplyNavigatedRoute(e);
    }

    private void ApplyNavigatedRoute(Abstractions.Navigation.NavigationEventArgs e)
    {
        if (_navigationService is WinUiNavigationService winUiNav)
        {
            var descriptor = winUiNav.FindRoute(e.Route);
            if (descriptor is not null)
            {
                PageContentHost.Content = descriptor.ViewFactory();
                TitleBar.SubtitleText = descriptor.Title;
            }
        }
        else
        {
            var match = _navigationService?.RegisteredRoutes.FirstOrDefault(r =>
                string.Equals(r.Route, e.Route, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                PageContentHost.Content = match.ViewFactory();
                TitleBar.SubtitleText = match.Title;
            }
        }

        CanGoBack = _navigationService?.CanGoBack ?? false;

        _syncingSelection = true;
        try
        {
            var allItems = ShellNavigationView.MenuItems
                .Concat(ShellNavigationView.FooterMenuItems)
                .OfType<NavigationViewItem>();
            ShellNavigationView.SelectedItem = allItems.FirstOrDefault(i =>
                string.Equals(i.Tag as string, e.Route, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void ShellNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSelection || _navigationService is null)
            return;

        if (args.SelectedItemContainer?.Tag is string route)
        {
            _navigationService.Navigate(route);
        }
    }

    private void ShellNavigationView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        _navigationService?.GoBack();
    }

    private void OnThemeToggleRequested(object? sender, EventArgs e)
    {
        if (_themeService is null)
            return;

        var next = _themeService.IsDarkEffective ? AppThemeMode.Light : AppThemeMode.Dark;
        _themeService.ApplyTheme(next);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
