using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Abstractions.Threading;
using NativeShellKit.WinUI.Navigation;
using NativeShellKit.WinUI.Theming;
using Windows.System;

namespace NativeShellKit.WinUI.Shell;

public sealed partial class NativeShellFrame : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(ShellMode), typeof(NativeShellFrame),
        new PropertyMetadata(ShellMode.Dashboard, OnShellLayoutPropertyChanged));

    public static readonly DependencyProperty DensityProperty = DependencyProperty.Register(
        nameof(Density), typeof(ShellDensity), typeof(NativeShellFrame),
        new PropertyMetadata(ShellDensity.Standard, OnShellLayoutPropertyChanged));

    public static readonly DependencyProperty AppTitleProperty = DependencyProperty.Register(
        nameof(AppTitle), typeof(string), typeof(NativeShellFrame),
        new PropertyMetadata("NativeShellKit", (d, e) => ((NativeShellFrame)d).TitleBar.Title = e.NewValue as string ?? string.Empty));

    public static readonly DependencyProperty AppSubtitleProperty = DependencyProperty.Register(
        nameof(AppSubtitle), typeof(string), typeof(NativeShellFrame),
        new PropertyMetadata(null, (d, e) => ((NativeShellFrame)d).TitleBar.Subtitle = e.NewValue as string));

    public static readonly DependencyProperty AppIconSourceProperty = DependencyProperty.Register(
        nameof(AppIconSource), typeof(ImageSource), typeof(NativeShellFrame),
        new PropertyMetadata(null, (d, e) => ((NativeShellFrame)d).TitleBar.IconSource = e.NewValue as ImageSource));

    public static readonly DependencyProperty AppIconGlyphProperty = DependencyProperty.Register(
        nameof(AppIconGlyph), typeof(string), typeof(NativeShellFrame),
        new PropertyMetadata("\uE756", (d, e) => ((NativeShellFrame)d).TitleBar.IconGlyph = e.NewValue as string ?? "\uE756"));

    public static readonly DependencyProperty TitleBarSearchContentProperty = DependencyProperty.Register(
        nameof(TitleBarSearchContent), typeof(object), typeof(NativeShellFrame),
        new PropertyMetadata(null, (d, e) => ((NativeShellFrame)d).TitleBar.CenterContent = e.NewValue));

    public static readonly DependencyProperty TitleBarStatusContentProperty = DependencyProperty.Register(
        nameof(TitleBarStatusContent), typeof(object), typeof(NativeShellFrame),
        new PropertyMetadata(null, (d, e) => ((NativeShellFrame)d).TitleBar.StatusContent = e.NewValue));

    public static readonly DependencyProperty TitleBarActionsContentProperty = DependencyProperty.Register(
        nameof(TitleBarActionsContent), typeof(object), typeof(NativeShellFrame),
        new PropertyMetadata(null, (d, e) => ((NativeShellFrame)d).TitleBar.RightContent = e.NewValue));

    public static readonly DependencyProperty FooterStripContentProperty = DependencyProperty.Register(
        nameof(FooterStripContent), typeof(object), typeof(NativeShellFrame),
        new PropertyMetadata(null, OnFooterStripChanged));

    public static readonly DependencyProperty ShowBackButtonProperty = DependencyProperty.Register(
        nameof(ShowBackButton), typeof(bool), typeof(NativeShellFrame),
        new PropertyMetadata(true, OnShellLayoutPropertyChanged));

    public static readonly DependencyProperty ShowThemeToggleProperty = DependencyProperty.Register(
        nameof(ShowThemeToggle), typeof(bool), typeof(NativeShellFrame),
        new PropertyMetadata(true, (d, e) => ((NativeShellFrame)d).TitleBar.ShowThemeToggle = (bool)e.NewValue));

    public static readonly DependencyProperty WideBreakpointWidthProperty = DependencyProperty.Register(
        nameof(WideBreakpointWidth), typeof(double), typeof(NativeShellFrame),
        new PropertyMetadata(1008.0, OnShellLayoutPropertyChanged));

    public static readonly DependencyProperty CompactBreakpointWidthProperty = DependencyProperty.Register(
        nameof(CompactBreakpointWidth), typeof(double), typeof(NativeShellFrame),
        new PropertyMetadata(641.0, OnShellLayoutPropertyChanged));

    private sealed class ManagedBannerItem
    {
        public required InfoBar Control { get; init; }
        public DispatcherTimer? Timer { get; init; }
        public bool IsPaused { get; set; }
    }

    private readonly Dictionary<string, object> _cachedViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ManagedBannerItem> _activeBanners = [];
    private INavigationService? _navigationService;
    private IThemeService? _themeService;
    private INotificationService? _notificationService;
    private IUiDispatcher? _uiDispatcher;
    private Window? _attachedWindow;
    private object? _currentView;
    private bool _syncingSelection;
    private bool _canGoBack;
    private int _coalescedExtraBanners;
    private bool _acceleratorsAttached;

    public NativeShellFrame()
    {
        InitializeComponent();
        TitleBar.ThemeToggleRequested += OnThemeToggleRequested;
        SizeChanged += OnFrameSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ApplyShellModeAndResponsiveLayout(ActualWidth);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ShellTitleBar ShellTitleBar => TitleBar;
    public NavigationView NavigationControl => ShellNavigationView;
    public SelectorBar UtilityNavigationBar => CompactSelectorBar;

    public ShellMode Mode
    {
        get => (ShellMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public ShellDensity Density
    {
        get => (ShellDensity)GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    public string AppTitle
    {
        get => (string)GetValue(AppTitleProperty);
        set => SetValue(AppTitleProperty, value);
    }

    public string? AppSubtitle
    {
        get => (string?)GetValue(AppSubtitleProperty);
        set => SetValue(AppSubtitleProperty, value);
    }

    public ImageSource? AppIconSource
    {
        get => (ImageSource?)GetValue(AppIconSourceProperty);
        set => SetValue(AppIconSourceProperty, value);
    }

    public string AppIconGlyph
    {
        get => (string)GetValue(AppIconGlyphProperty);
        set => SetValue(AppIconGlyphProperty, value);
    }

    public object? TitleBarSearchContent
    {
        get => GetValue(TitleBarSearchContentProperty);
        set => SetValue(TitleBarSearchContentProperty, value);
    }

    public object? TitleBarStatusContent
    {
        get => GetValue(TitleBarStatusContentProperty);
        set => SetValue(TitleBarStatusContentProperty, value);
    }

    public object? TitleBarActionsContent
    {
        get => GetValue(TitleBarActionsContentProperty);
        set => SetValue(TitleBarActionsContentProperty, value);
    }

    public object? FooterStripContent
    {
        get => GetValue(FooterStripContentProperty);
        set => SetValue(FooterStripContentProperty, value);
    }

    public bool ShowBackButton
    {
        get => (bool)GetValue(ShowBackButtonProperty);
        set => SetValue(ShowBackButtonProperty, value);
    }

    public bool ShowThemeToggle
    {
        get => (bool)GetValue(ShowThemeToggleProperty);
        set => SetValue(ShowThemeToggleProperty, value);
    }

    public double WideBreakpointWidth
    {
        get => (double)GetValue(WideBreakpointWidthProperty);
        set => SetValue(WideBreakpointWidthProperty, value);
    }

    public double CompactBreakpointWidth
    {
        get => (double)GetValue(CompactBreakpointWidthProperty);
        set => SetValue(CompactBreakpointWidthProperty, value);
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        private set
        {
            if (_canGoBack != value)
            {
                _canGoBack = value;
                ShellNavigationView.IsBackEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public void Configure(ShellConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Mode = configuration.Mode;
        Density = configuration.Density;
        ShowBackButton = configuration.Mode is ShellMode.Dashboard or ShellMode.ModuleHost;
    }

    public IDisposable AttachServices(
        INavigationService navigationService,
        IThemeService themeService,
        INotificationService? notificationService = null,
        IShellWindowService? windowService = null,
        IUiDispatcher? uiDispatcher = null,
        Window? window = null,
        string? appTitle = null)
    {
        DetachServices();

        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _notificationService = notificationService;
        _uiDispatcher = uiDispatcher;
        _attachedWindow = window;

        if (!string.IsNullOrWhiteSpace(appTitle))
            AppTitle = appTitle;

        if (themeService is WinUiThemeService winUiTheme)
        {
            winUiTheme.AttachRoot(this);
            if (window is not null)
                winUiTheme.TryApplyBackdrop(window);
        }

        if (window is not null)
        {
            if (windowService is ShellWindowService shellWindowService)
                shellWindowService.AttachWindow(window, TitleBar);
            else
                TitleBar.AttachWindow(window);
        }

        _navigationService.Navigated += OnNavigated;
        if (_notificationService is not null)
            _notificationService.InAppNotificationRaised += OnInAppNotificationRaised;

        RebuildNavigationItems();

        if (_navigationService.CurrentRoute is null && _navigationService.RegisteredRoutes.Count > 0)
        {
            _navigationService.Navigate(_navigationService.RegisteredRoutes[0].Route);
        }
        else if (_navigationService.CurrentRoute is not null)
        {
            var current = _navigationService.RegisteredRoutes.FirstOrDefault(r =>
                string.Equals(r.Route, _navigationService.CurrentRoute, StringComparison.OrdinalIgnoreCase));
            if (current is not null)
            {
                ApplyNavigatedRoute(new Abstractions.Navigation.NavigationEventArgs(current.Route, null, null));
            }
        }

        return new AttachmentHandle(this);
    }

    public void DetachServices()
    {
        if (_navigationService is not null)
        {
            _navigationService.Navigated -= OnNavigated;
            _navigationService = null;
        }
        if (_notificationService is not null)
        {
            _notificationService.InAppNotificationRaised -= OnInAppNotificationRaised;
            _notificationService = null;
        }
        _themeService = null;
        _uiDispatcher = null;
        _attachedWindow = null;

        foreach (var item in _activeBanners)
            item.Timer?.Stop();
        _activeBanners.Clear();
    }

    public void RebuildNavigationItems()
    {
        if (_navigationService is null)
            return;

        ShellNavigationView.MenuItems.Clear();
        ShellNavigationView.FooterMenuItems.Clear();
        CompactSelectorBar.Items.Clear();

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

            var tabItem = new SelectorBarItem
            {
                Text = route.Title,
                Tag = route.Route,
                Icon = new FontIcon { Glyph = string.IsNullOrWhiteSpace(route.Glyph) ? "\uE80F" : route.Glyph }
            };
            AutomationProperties.SetName(tabItem, route.Title);
            CompactSelectorBar.Items.Add(tabItem);
        }

        SyncSelectedRoute(_navigationService.CurrentRoute);
    }

    public void SetBannerContent(object? content)
    {
        EnqueueOnUi(() =>
        {
            CustomBannerPresenter.Content = content;
            CustomBannerPresenter.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
            UpdateBannerHostVisibility();
        });
    }

    public void ClearViewCache()
    {
        _cachedViews.Clear();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachKeyboardAccelerators();
        ApplyShellModeAndResponsiveLayout(ActualWidth);
        if (_attachedWindow is not null)
            TitleBar.AttachWindow(_attachedWindow);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachKeyboardAccelerators();
        DetachServices();
    }

    private void AttachKeyboardAccelerators()
    {
        if (_acceleratorsAttached)
            return;
        _acceleratorsAttached = true;

        var searchAccel = new KeyboardAccelerator { Key = VirtualKey.K, Modifiers = VirtualKeyModifiers.Control };
        searchAccel.Invoked += OnSearchAcceleratorInvoked;
        ShellRootGrid.KeyboardAccelerators.Add(searchAccel);

        var backAccel = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        backAccel.Invoked += OnBackAcceleratorInvoked;
        ShellRootGrid.KeyboardAccelerators.Add(backAccel);

        var settingsAccel = new KeyboardAccelerator { Key = (VirtualKey)188, Modifiers = VirtualKeyModifiers.Control }; // Ctrl+,
        settingsAccel.Invoked += OnSettingsAcceleratorInvoked;
        ShellRootGrid.KeyboardAccelerators.Add(settingsAccel);
    }

    private void DetachKeyboardAccelerators()
    {
        if (!_acceleratorsAttached)
            return;
        _acceleratorsAttached = false;
        ShellRootGrid.KeyboardAccelerators.Clear();
    }

    private void OnSearchAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = TitleBar.FocusSearchSlot();
    }

    private void OnBackAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_navigationService?.CanGoBack == true)
        {
            args.Handled = _navigationService.GoBack();
        }
    }

    private void OnSettingsAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var settingsRoute = _navigationService?.RegisteredRoutes.FirstOrDefault(r =>
            string.Equals(r.Route, "settings", StringComparison.OrdinalIgnoreCase));
        if (settingsRoute is not null)
        {
            args.Handled = _navigationService!.Navigate(settingsRoute.Route);
        }
    }

    private void OnFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyShellModeAndResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyShellModeAndResponsiveLayout(double width)
    {
        TitleBar.Compact = Mode == ShellMode.CompactUtility || Density == ShellDensity.Compact;

        ContentContainerGrid.Padding = Density == ShellDensity.Compact
            ? new Thickness(16, 10, 16, 14)
            : new Thickness(24, 16, 24, 24);
        ContentContainerGrid.RowSpacing = Density == ShellDensity.Compact ? 8 : 12;

        if (Mode == ShellMode.SinglePage)
        {
            ShellNavigationView.IsPaneVisible = false;
            ShellNavigationView.IsPaneToggleButtonVisible = false;
            ShellNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
            ShellNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
            CompactSelectorBar.Visibility = Visibility.Collapsed;
            return;
        }

        if (Mode == ShellMode.CompactUtility)
        {
            ShellNavigationView.IsPaneVisible = false;
            ShellNavigationView.IsPaneToggleButtonVisible = false;
            ShellNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
            ShellNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
            CompactSelectorBar.Visibility = Visibility.Visible;
            return;
        }

        // Dashboard or ModuleHost
        CompactSelectorBar.Visibility = Visibility.Collapsed;
        ShellNavigationView.IsPaneVisible = true;
        ShellNavigationView.IsPaneToggleButtonVisible = true;
        ShellNavigationView.IsBackButtonVisible = ShowBackButton
            ? NavigationViewBackButtonVisible.Auto
            : NavigationViewBackButtonVisible.Collapsed;

        if (width <= 0 || width >= WideBreakpointWidth)
            ShellNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        else if (width >= CompactBreakpointWidth)
            ShellNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
        else
            ShellNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
    }

    private void OnNavigated(object? sender, Abstractions.Navigation.NavigationEventArgs e)
    {
        EnqueueOnUi(() => ApplyNavigatedRoute(e));
    }

    private void ApplyNavigatedRoute(Abstractions.Navigation.NavigationEventArgs e)
    {
        var descriptor = _navigationService is WinUiNavigationService winUiNav
            ? winUiNav.FindRoute(e.Route)
            : _navigationService?.RegisteredRoutes.FirstOrDefault(r =>
                string.Equals(r.Route, e.Route, StringComparison.OrdinalIgnoreCase));

        if (descriptor is not null)
        {
            if (_currentView is INavigationAware prevAware)
            {
                try { prevAware.OnNavigatedFrom(); } catch { }
            }

            object view;
            if (descriptor.CachePolicy == NavigationCachePolicy.Cached)
            {
                if (!_cachedViews.TryGetValue(descriptor.Route, out view!))
                {
                    view = descriptor.ViewFactory();
                    _cachedViews[descriptor.Route] = view;
                }
            }
            else
            {
                view = descriptor.ViewFactory();
            }

            bool animationsEnabled = (_themeService as WinUiThemeService)?.IsAnimationsEnabled ?? true;
            var transitions = new TransitionCollection();
            if (animationsEnabled)
                transitions.Add(new ContentThemeTransition());
            PageContentHost.ContentTransitions = transitions;

            PageContentHost.Content = view;
            _currentView = view;

            if (view is INavigationAware nextAware)
            {
                try { nextAware.OnNavigatedTo(e.Parameter); } catch { }
            }

            AppSubtitle = descriptor.Title;
        }

        CanGoBack = _navigationService?.CanGoBack ?? false;
        SyncSelectedRoute(e.Route);
    }

    private void SyncSelectedRoute(string? route)
    {
        _syncingSelection = true;
        try
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                ShellNavigationView.SelectedItem = null;
                CompactSelectorBar.SelectedItem = null;
                return;
            }

            var allItems = ShellNavigationView.MenuItems
                .Concat(ShellNavigationView.FooterMenuItems)
                .OfType<NavigationViewItem>();
            ShellNavigationView.SelectedItem = allItems.FirstOrDefault(i =>
                string.Equals(i.Tag as string, route, StringComparison.OrdinalIgnoreCase));

            CompactSelectorBar.SelectedItem = CompactSelectorBar.Items
                .FirstOrDefault(i => string.Equals(i.Tag as string, route, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnInAppNotificationRaised(object? sender, NotificationRecord record)
    {
        EnqueueOnUi(() => AddManagedBanner(record));
    }

    private void AddManagedBanner(NotificationRecord record)
    {
        while (_activeBanners.Count >= 2)
        {
            int oldestNonErrorIndex = _activeBanners.FindIndex(b => b.Control.Severity != InfoBarSeverity.Error);
            int removeIdx = oldestNonErrorIndex >= 0 ? oldestNonErrorIndex : 0;
            RemoveManagedBanner(_activeBanners[removeIdx]);
            _coalescedExtraBanners++;
        }

        var infoBar = new InfoBar
        {
            Title = record.Title,
            Message = record.Body,
            IsOpen = true,
            IsClosable = true,
            Severity = record.Severity switch
            {
                NotificationSeverity.Success => InfoBarSeverity.Success,
                NotificationSeverity.Warning => InfoBarSeverity.Warning,
                NotificationSeverity.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            }
        };

        AutomationProperties.SetLiveSetting(
            infoBar,
            record.Severity == NotificationSeverity.Error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);

        DispatcherTimer? timer = null;
        if (record.Severity != NotificationSeverity.Error)
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        }

        var entry = new ManagedBannerItem { Control = infoBar, Timer = timer };

        if (timer is not null)
        {
            timer.Tick += (_, _) =>
            {
                if (!entry.IsPaused)
                {
                    timer.Stop();
                    RemoveManagedBanner(entry);
                }
            };
            infoBar.PointerEntered += (_, _) => entry.IsPaused = true;
            infoBar.PointerExited += (_, _) => entry.IsPaused = false;
            infoBar.GotFocus += (_, _) => entry.IsPaused = true;
            infoBar.LostFocus += (_, _) => entry.IsPaused = false;
            timer.Start();
        }

        infoBar.Closed += (_, _) => RemoveManagedBanner(entry);

        _activeBanners.Add(entry);
        ManagedBannersPanel.Children.Add(infoBar);
        UpdateBannerHostVisibility();
    }

    private void RemoveManagedBanner(ManagedBannerItem entry)
    {
        entry.Timer?.Stop();
        _activeBanners.Remove(entry);
        ManagedBannersPanel.Children.Remove(entry.Control);
        if (_activeBanners.Count == 0)
            _coalescedExtraBanners = 0;
        UpdateBannerHostVisibility();
    }

    private void UpdateBannerHostVisibility()
    {
        bool hasCustom = CustomBannerPresenter.Content is not null;
        bool hasManaged = _activeBanners.Count > 0;
        CoalescedBannerCountText.Visibility = _coalescedExtraBanners > 0 ? Visibility.Visible : Visibility.Collapsed;
        CoalescedBannerCountText.Text = _coalescedExtraBanners > 0 ? $"+{_coalescedExtraBanners}" : string.Empty;
        BuiltInBannerStackPanel.Visibility = (hasCustom || hasManaged) ? Visibility.Visible : Visibility.Collapsed;
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

    private void CompactSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncingSelection || _navigationService is null)
            return;

        if (sender.SelectedItem?.Tag is string route)
        {
            _navigationService.Navigate(route);
        }
    }

    private void OnThemeToggleRequested(object? sender, EventArgs e)
    {
        if (_themeService is null)
            return;

        var next = _themeService.IsDarkEffective ? AppThemeMode.Light : AppThemeMode.Dark;
        _themeService.ApplyTheme(next);
    }

    private void EnqueueOnUi(Action action)
    {
        if (_uiDispatcher is not null)
        {
            _uiDispatcher.TryEnqueue(action);
            return;
        }

        if (DispatcherQueue is { HasThreadAccess: false } queue)
        {
            queue.TryEnqueue(() => action());
            return;
        }

        action();
    }

    private static void OnShellLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var frame = (NativeShellFrame)d;
        frame.ApplyShellModeAndResponsiveLayout(frame.ActualWidth);
    }

    private static void OnFooterStripChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var frame = (NativeShellFrame)d;
        frame.FooterStripPresenter.Content = e.NewValue;
        frame.FooterStripPresenter.Visibility = e.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class AttachmentHandle(NativeShellFrame owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.DetachServices();
        }
    }
}
