using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

public partial class StatusBadge : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ShellStatusKind), typeof(StatusBadge),
        new PropertyMetadata(ShellStatusKind.Healthy, OnVisualPropertyChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(StatusBadge),
        new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(StatusBadge),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty ShowDotProperty = DependencyProperty.Register(
        nameof(ShowDot), typeof(bool), typeof(StatusBadge),
        new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty AnnounceChangesProperty = DependencyProperty.Register(
        nameof(AnnounceChanges), typeof(bool), typeof(StatusBadge),
        new PropertyMetadata(false, OnAnnounceChangesChanged));

    public StatusBadge()
    {
        DefaultStyleKey = typeof(StatusBadge);
        Loaded += (_, _) => ApplyVisualState(false);
    }

    public ShellStatusKind State
    {
        get => (ShellStatusKind)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public bool ShowDot
    {
        get => (bool)GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }

    public bool AnnounceChanges
    {
        get => (bool)GetValue(AnnounceChangesProperty);
        set => SetValue(AnnounceChangesProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ApplyVisualState(false);
        UpdateAutomation();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (StatusBadge)d;
        ctl.ApplyVisualState(true);
        ctl.UpdateAutomation();
    }

    private static void OnAnnounceChangesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (StatusBadge)d;
        AutomationProperties.SetLiveSetting(
            ctl,
            ctl.AnnounceChanges ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
    }

    private void UpdateAutomation()
    {
        string text = string.IsNullOrWhiteSpace(Label) ? State.ToString() : $"{Label} ({State})";
        if (string.IsNullOrEmpty(AutomationProperties.GetName(this)))
        {
            AutomationProperties.SetName(this, text);
        }

        if (Compact && !string.IsNullOrWhiteSpace(Label))
        {
            ToolTipService.SetToolTip(this, text);
        }
    }

    private void ApplyVisualState(bool useTransitions)
    {
        string stateName = State switch
        {
            ShellStatusKind.Healthy => "Healthy",
            ShellStatusKind.Active => "Active",
            ShellStatusKind.Warning => "Warning",
            ShellStatusKind.Error => "Error",
            ShellStatusKind.Offline => "Offline",
            ShellStatusKind.Syncing => "Syncing",
            _ => "Healthy"
        };
        VisualStateManager.GoToState(this, stateName, useTransitions);
        VisualStateManager.GoToState(this, Compact ? "CompactOn" : "CompactOff", useTransitions);
        VisualStateManager.GoToState(this, ShowDot ? "DotVisible" : "DotCollapsed", useTransitions);
    }
}
