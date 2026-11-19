using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace NativeShellKit.DesignSystem.Controls;

public partial class MetricCard : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(MetricCard),
        new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(MetricCard),
        new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(MetricCard),
        new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(MetricCard),
        new PropertyMetadata("\uE9D9", OnVisualChanged));

    public static readonly DependencyProperty TrendStateProperty = DependencyProperty.Register(
        nameof(TrendState), typeof(ShellStatusKind?), typeof(MetricCard),
        new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading), typeof(bool), typeof(MetricCard),
        new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(MetricCard),
        new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(MetricCard),
        new PropertyMetadata(null));

    private bool _pointerOver;
    private bool _pressed;

    public MetricCard()
    {
        DefaultStyleKey = typeof(MetricCard);
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        KeyDown += OnKeyDown;
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Caption
    {
        get => (string?)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public ShellStatusKind? TrendState
    {
        get => (ShellStatusKind?)GetValue(TrendStateProperty);
        set => SetValue(TrendStateProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateVisualStates(false);
        UpdateAutomationName();
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MetricCard)d).UpdateAutomationName();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MetricCard)d).UpdateVisualStates(true);
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (MetricCard)d;
        ctl.IsTabStop = ctl.Command is not null;
        ctl.UpdateVisualStates(false);
    }

    private void UpdateAutomationName()
    {
        AutomationProperties.SetName(this, $"{Label}: {Value}");
    }

    private void UpdateVisualStates(bool useTransitions)
    {
        VisualStateManager.GoToState(this, IsLoading ? "Loading" : "Ready", useTransitions);
        VisualStateManager.GoToState(this, string.IsNullOrWhiteSpace(Caption) ? "CaptionCollapsed" : "CaptionVisible", useTransitions);
        VisualStateManager.GoToState(this, string.IsNullOrWhiteSpace(Glyph) ? "GlyphCollapsed" : "GlyphVisible", useTransitions);

        string trend = TrendState switch
        {
            ShellStatusKind.Healthy => "TrendHealthy",
            ShellStatusKind.Warning => "TrendWarning",
            ShellStatusKind.Error => "TrendError",
            ShellStatusKind.Active => "TrendActive",
            _ => "TrendNeutral"
        };
        VisualStateManager.GoToState(this, trend, useTransitions);

        if (Command is null)
            VisualStateManager.GoToState(this, "Normal", useTransitions);
        else if (_pressed)
            VisualStateManager.GoToState(this, "Pressed", useTransitions);
        else if (_pointerOver)
            VisualStateManager.GoToState(this, "PointerOver", useTransitions);
        else
            VisualStateManager.GoToState(this, "Normal", useTransitions);
    }

    private void InvokeCommand()
    {
        var cmd = Command;
        if (cmd is not null && cmd.CanExecute(CommandParameter))
            cmd.Execute(CommandParameter);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = true;
        UpdateVisualStates(true);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = false;
        _pressed = false;
        UpdateVisualStates(true);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Command is null)
            return;
        _pressed = true;
        UpdateVisualStates(true);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed)
        {
            _pressed = false;
            UpdateVisualStates(true);
            InvokeCommand();
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        UpdateVisualStates(true);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Command is not null && e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            e.Handled = true;
            InvokeCommand();
        }
    }
}

public sealed class CompactMetricCard : MetricCard
{
}

public sealed partial class MetricStrip : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(MetricStrip),
        new PropertyMetadata(12.0, (d, _) => ((MetricStrip)d).InvalidateMeasure()));

    public static readonly DependencyProperty WideColumnsProperty = DependencyProperty.Register(
        nameof(WideColumns), typeof(int), typeof(MetricStrip),
        new PropertyMetadata(4, (d, _) => ((MetricStrip)d).InvalidateMeasure()));

    public static readonly DependencyProperty MediumColumnsProperty = DependencyProperty.Register(
        nameof(MediumColumns), typeof(int), typeof(MetricStrip),
        new PropertyMetadata(2, (d, _) => ((MetricStrip)d).InvalidateMeasure()));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public int WideColumns
    {
        get => (int)GetValue(WideColumnsProperty);
        set => SetValue(WideColumnsProperty, value);
    }

    public int MediumColumns
    {
        get => (int)GetValue(MediumColumnsProperty);
        set => SetValue(MediumColumnsProperty, value);
    }

    private int ResolveColumns(double width)
    {
        if (double.IsInfinity(width) || width >= 1008)
            return Math.Max(1, Math.Min(WideColumns, Math.Max(1, Children.Count)));
        if (width >= 641)
            return Math.Max(1, Math.Min(MediumColumns, Math.Max(1, Children.Count)));
        return 1;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
            return new Size(0, 0);

        double width = double.IsInfinity(availableSize.Width) ? 1024 : availableSize.Width;
        int cols = ResolveColumns(width);
        double gap = Spacing;
        double cellWidth = Math.Max(0, (width - gap * (cols - 1)) / cols);

        double totalHeight = 0;
        int rows = (int)Math.Ceiling(Children.Count / (double)cols);

        for (int r = 0; r < rows; r++)
        {
            double rowHeight = 0;
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                if (idx >= Children.Count)
                    break;
                Children[idx].Measure(new Size(cellWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, Children[idx].DesiredSize.Height);
            }
            totalHeight += rowHeight + (r > 0 ? gap : 0);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? cols * cellWidth + gap * (cols - 1) : availableSize.Width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
            return finalSize;

        int cols = ResolveColumns(finalSize.Width);
        double gap = Spacing;
        double cellWidth = Math.Max(0, (finalSize.Width - gap * (cols - 1)) / cols);

        double y = 0;
        int rows = (int)Math.Ceiling(Children.Count / (double)cols);
        for (int r = 0; r < rows; r++)
        {
            double rowHeight = 0;
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                if (idx >= Children.Count)
                    break;
                rowHeight = Math.Max(rowHeight, Children[idx].DesiredSize.Height);
            }

            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                if (idx >= Children.Count)
                    break;
                double x = c * (cellWidth + gap);
                Children[idx].Arrange(new Rect(x, y, cellWidth, rowHeight));
            }

            y += rowHeight + gap;
        }

        return finalSize;
    }
}
