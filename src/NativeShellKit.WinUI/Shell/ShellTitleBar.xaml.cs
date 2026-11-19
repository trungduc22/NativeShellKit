using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace NativeShellKit.WinUI.Shell;

public sealed partial class ShellTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ShellTitleBar),
        new PropertyMetadata("NativeShellKit", OnTitleChanged));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(ShellTitleBar),
        new PropertyMetadata(null, OnSubtitleChanged));

    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(
        nameof(IconSource), typeof(ImageSource), typeof(ShellTitleBar),
        new PropertyMetadata(null, OnIconChanged));

    public static readonly DependencyProperty IconGlyphProperty = DependencyProperty.Register(
        nameof(IconGlyph), typeof(string), typeof(ShellTitleBar),
        new PropertyMetadata("\uE756", OnIconChanged));

    public static readonly DependencyProperty CenterContentProperty = DependencyProperty.Register(
        nameof(CenterContent), typeof(object), typeof(ShellTitleBar),
        new PropertyMetadata(null, OnSlotContentChanged));

    public static readonly DependencyProperty StatusContentProperty = DependencyProperty.Register(
        nameof(StatusContent), typeof(object), typeof(ShellTitleBar),
        new PropertyMetadata(null, OnSlotContentChanged));

    public static readonly DependencyProperty RightContentProperty = DependencyProperty.Register(
        nameof(RightContent), typeof(object), typeof(ShellTitleBar),
        new PropertyMetadata(null, OnSlotContentChanged));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(ShellTitleBar),
        new PropertyMetadata(false, OnCompactChanged));

    public static readonly DependencyProperty ShowThemeToggleProperty = DependencyProperty.Register(
        nameof(ShowThemeToggle), typeof(bool), typeof(ShellTitleBar),
        new PropertyMetadata(true, OnShowThemeToggleChanged));

    public static readonly DependencyProperty ThemeToggleLabelProperty = DependencyProperty.Register(
        nameof(ThemeToggleLabel), typeof(string), typeof(ShellTitleBar),
        new PropertyMetadata("Theme", OnThemeToggleLabelChanged));

    private Window? _attachedWindow;
    private XamlRoot? _trackedXamlRoot;
    private bool _eventsHooked;
    private readonly List<RectInt32> _lastPassthroughRects = [];

    public ShellTitleBar()
    {
        InitializeComponent();
        ApplyAllVisuals();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    public event EventHandler? ThemeToggleRequested;

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public object? StatusContent
    {
        get => GetValue(StatusContentProperty);
        set => SetValue(StatusContentProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public bool ShowThemeToggle
    {
        get => (bool)GetValue(ShowThemeToggleProperty);
        set => SetValue(ShowThemeToggleProperty, value);
    }

    public string ThemeToggleLabel
    {
        get => (string)GetValue(ThemeToggleLabelProperty);
        set => SetValue(ThemeToggleLabelProperty, value);
    }

    public IReadOnlyList<RectInt32> LastPassthroughRects => _lastPassthroughRects;

    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _attachedWindow = window;
        HookXamlRoot();
        UpdateCaptionInsetsAndPassthroughRegions();
    }

    public bool FocusSearchSlot()
    {
        if (CenterSlotPresenter.Content is not DependencyObject root)
            return false;

        var box = FindDescendant<AutoSuggestBox>(root);
        if (box is not null)
        {
            box.Focus(FocusState.Programmatic);
            var textBox = FindDescendant<TextBox>(box);
            textBox?.SelectAll();
            return true;
        }

        var tb = FindDescendant<TextBox>(root);
        if (tb is not null)
        {
            tb.Focus(FocusState.Programmatic);
            tb.SelectAll();
            return true;
        }

        if (root is Control ctrl)
        {
            return ctrl.Focus(FocusState.Programmatic);
        }

        return false;
    }

    public void RefreshPassthroughRegions() => UpdateCaptionInsetsAndPassthroughRegions();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookXamlRoot();
        UpdateCaptionInsetsAndPassthroughRegions();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_trackedXamlRoot is not null && _eventsHooked)
        {
            _trackedXamlRoot.Changed -= OnXamlRootChanged;
            _eventsHooked = false;
        }
        _trackedXamlRoot = null;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCaptionInsetsAndPassthroughRegions();
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        UpdateCaptionInsetsAndPassthroughRegions();
    }

    private void HookXamlRoot()
    {
        if (_trackedXamlRoot == XamlRoot && _eventsHooked)
            return;

        if (_trackedXamlRoot is not null && _eventsHooked)
        {
            _trackedXamlRoot.Changed -= OnXamlRootChanged;
            _eventsHooked = false;
        }

        _trackedXamlRoot = XamlRoot;
        if (_trackedXamlRoot is not null)
        {
            _trackedXamlRoot.Changed += OnXamlRootChanged;
            _eventsHooked = true;
        }
    }

    private void UpdateCaptionInsetsAndPassthroughRegions()
    {
        if (_attachedWindow is null)
            return;

        try
        {
            double scale = XamlRoot?.RasterizationScale > 0 ? XamlRoot.RasterizationScale : 1.0;
            var hwnd = WindowNative.GetWindowHandle(_attachedWindow);
            if (hwnd == nint.Zero)
                return;

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            double leftPad = 16;
            double rightPad = 148;
            if (AppWindowTitleBar.IsCustomizationSupported() && appWindow.TitleBar.ExtendsContentIntoTitleBar)
            {
                if (appWindow.TitleBar.LeftInset > 0)
                    leftPad = Math.Max(12, appWindow.TitleBar.LeftInset / scale);
                if (appWindow.TitleBar.RightInset > 0)
                    rightPad = Math.Max(48, appWindow.TitleBar.RightInset / scale);
            }

            TitleBarRoot.Padding = new Thickness(leftPad, 0, rightPad, 0);

            if (_attachedWindow.Content is not UIElement windowRoot)
                return;

            var rects = new List<RectInt32>();
            CollectPassthroughRect(CenterSlotPresenter, windowRoot, scale, rects);
            CollectPassthroughRect(StatusSlotPresenter, windowRoot, scale, rects);
            CollectPassthroughRect(RightSlotPresenter, windowRoot, scale, rects);
            if (ThemeToggleButton.Visibility == Visibility.Visible)
                CollectPassthroughRect(ThemeToggleButton, windowRoot, scale, rects);

            _lastPassthroughRects.Clear();
            _lastPassthroughRects.AddRange(rects);

            var nonClient = InputNonClientPointerSource.GetForWindowId(windowId);
            nonClient.SetRegionRects(NonClientRegionKind.Passthrough, [.. rects]);
        }
        catch
        {
            // Safe when window handle/non-client input source is unavailable during early init or teardown.
        }
    }

    private static void CollectPassthroughRect(
        FrameworkElement element,
        UIElement windowRoot,
        double scale,
        List<RectInt32> output)
    {
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return;

        if (element is ContentPresenter cp && cp.Content is null)
            return;

        var transform = element.TransformToVisual(windowRoot);
        var bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        output.Add(new RectInt32(
            (int)Math.Floor(bounds.X * scale),
            (int)Math.Floor(bounds.Y * scale),
            (int)Math.Ceiling(bounds.Width * scale),
            (int)Math.Ceiling(bounds.Height * scale)));
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            return match;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }
        return null;
    }

    private void ApplyAllVisuals()
    {
        TitleTextBlock.Text = Title ?? string.Empty;
        SubtitleTextBlock.Text = Subtitle ?? string.Empty;
        SubtitleTextBlock.Visibility = string.IsNullOrWhiteSpace(Subtitle) ? Visibility.Collapsed : Visibility.Visible;

        if (IconSource is not null)
        {
            BrandImageIcon.Source = IconSource;
            BrandImageIcon.Visibility = Visibility.Visible;
            BrandGlyphIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            BrandImageIcon.Source = null;
            BrandImageIcon.Visibility = Visibility.Collapsed;
            BrandGlyphIcon.Glyph = string.IsNullOrWhiteSpace(IconGlyph) ? "\uE756" : IconGlyph;
            BrandGlyphIcon.Visibility = Visibility.Visible;
        }

        double h = Compact ? 36 : 44;
        Height = h;
        TitleBarRoot.Height = h;

        ThemeToggleButton.Visibility = ShowThemeToggle ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(ThemeToggleButton, ThemeToggleLabel ?? "Theme");
        ToolTipService.SetToolTip(ThemeToggleButton, ThemeToggleLabel ?? "Theme");

        CenterSlotPresenter.Content = CenterContent;
        StatusSlotPresenter.Content = StatusContent;
        RightSlotPresenter.Content = RightContent;
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (ShellTitleBar)d;
        ctl.TitleTextBlock.Text = e.NewValue as string ?? string.Empty;
    }

    private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (ShellTitleBar)d;
        var val = e.NewValue as string;
        ctl.SubtitleTextBlock.Text = val ?? string.Empty;
        ctl.SubtitleTextBlock.Visibility = string.IsNullOrWhiteSpace(val) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (ShellTitleBar)d;
        ctl.ApplyAllVisuals();
    }

    private static void OnSlotContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (ShellTitleBar)d;
        if (e.Property == CenterContentProperty)
            ctl.CenterSlotPresenter.Content = e.NewValue;
        else if (e.Property == StatusContentProperty)
            ctl.StatusSlotPresenter.Content = e.NewValue;
        else if (e.Property == RightContentProperty)
            ctl.RightSlotPresenter.Content = e.NewValue;

        ctl.UpdateCaptionInsetsAndPassthroughRegions();
    }

    private static void OnCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (ShellTitleBar)d;
        double h = ctl.Compact ? 36 : 44;
        ctl.Height = h;
        ctl.TitleBarRoot.Height = h;
        ctl.UpdateCaptionInsetsAndPassthroughRegions();
    }

    private static void OnShowThemeToggleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (ShellTitleBar)d;
        ctl.ThemeToggleButton.Visibility = ctl.ShowThemeToggle ? Visibility.Visible : Visibility.Collapsed;
        ctl.UpdateCaptionInsetsAndPassthroughRegions();
    }

    private static void OnThemeToggleLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (ShellTitleBar)d;
        var label = e.NewValue as string ?? "Theme";
        AutomationProperties.SetName(ctl.ThemeToggleButton, label);
        ToolTipService.SetToolTip(ctl.ThemeToggleButton, label);
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeToggleRequested?.Invoke(this, EventArgs.Empty);
    }
}
