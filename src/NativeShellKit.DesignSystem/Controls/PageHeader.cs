using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

public partial class PageHeader : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(PageHeader),
        new PropertyMetadata(null, OnLayoutPropertyChanged));

    public static readonly DependencyProperty BreadcrumbItemsProperty = DependencyProperty.Register(
        nameof(BreadcrumbItems), typeof(IReadOnlyList<string>), typeof(PageHeader),
        new PropertyMetadata(null, OnLayoutPropertyChanged));

    public static readonly DependencyProperty BadgeStateProperty = DependencyProperty.Register(
        nameof(BadgeState), typeof(ShellStatusKind), typeof(PageHeader),
        new PropertyMetadata(ShellStatusKind.Active));

    public static readonly DependencyProperty BadgeLabelProperty = DependencyProperty.Register(
        nameof(BadgeLabel), typeof(string), typeof(PageHeader),
        new PropertyMetadata(null, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ActionsContentProperty = DependencyProperty.Register(
        nameof(ActionsContent), typeof(object), typeof(PageHeader),
        new PropertyMetadata(null, OnLayoutPropertyChanged));

    public PageHeader()
    {
        DefaultStyleKey = typeof(PageHeader);
        SizeChanged += (_, e) => UpdateVisualStates(e.NewSize.Width, true);
        Loaded += (_, _) => UpdateVisualStates(ActualWidth, false);
    }

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

    public IReadOnlyList<string>? BreadcrumbItems
    {
        get => (IReadOnlyList<string>?)GetValue(BreadcrumbItemsProperty);
        set => SetValue(BreadcrumbItemsProperty, value);
    }

    public ShellStatusKind BadgeState
    {
        get => (ShellStatusKind)GetValue(BadgeStateProperty);
        set => SetValue(BadgeStateProperty, value);
    }

    public string? BadgeLabel
    {
        get => (string?)GetValue(BadgeLabelProperty);
        set => SetValue(BadgeLabelProperty, value);
    }

    public object? ActionsContent
    {
        get => GetValue(ActionsContentProperty);
        set => SetValue(ActionsContentProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateVisualStates(ActualWidth, false);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (PageHeader)d;
        ctl.UpdateVisualStates(ctl.ActualWidth, true);
    }

    private void UpdateVisualStates(double width, bool useTransitions)
    {
        if (GetTemplateChild("PART_Breadcrumb") is BreadcrumbBar breadcrumb)
        {
            var items = BreadcrumbItems;
            if (items is { Count: > 0 })
            {
                breadcrumb.ItemsSource = items;
                breadcrumb.Visibility = Visibility.Visible;
            }
            else
            {
                breadcrumb.Visibility = Visibility.Collapsed;
            }
        }

        VisualStateManager.GoToState(this, string.IsNullOrWhiteSpace(Subtitle) ? "SubtitleCollapsed" : "SubtitleVisible", useTransitions);
        VisualStateManager.GoToState(this, string.IsNullOrWhiteSpace(BadgeLabel) ? "BadgeCollapsed" : "BadgeVisible", useTransitions);
        VisualStateManager.GoToState(this, width > 0 && width <= 640 ? "NarrowLayout" : "WideLayout", useTransitions);
    }
}

public sealed class PageHeaderControl : PageHeader
{
    public static readonly DependencyProperty BadgeTextProperty = DependencyProperty.Register(
        nameof(BadgeText), typeof(string), typeof(PageHeaderControl),
        new PropertyMetadata(null, (d, e) => ((PageHeaderControl)d).BadgeLabel = e.NewValue as string));

    public string? BadgeText
    {
        get => (string?)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }
}
