using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

[Obsolete("Use StatusBadge instead. StatusIndicatorControl will be removed in 0.3.0.")]
public sealed partial class StatusIndicatorControl : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ShellIndicatorState), typeof(StatusIndicatorControl),
        new PropertyMetadata(ShellIndicatorState.Healthy, OnStateChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(StatusIndicatorControl),
        new PropertyMetadata(string.Empty, OnLabelChanged));

    public StatusIndicatorControl()
    {
        InitializeComponent();
        SyncState();
    }

    public ShellIndicatorState State
    {
        get => (ShellIndicatorState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatusIndicatorControl)d).SyncState();

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatusIndicatorControl)d).SyncLabel();

    private void SyncState()
    {
        InnerBadge.State = State switch
        {
            ShellIndicatorState.Healthy => ShellStatusKind.Healthy,
            ShellIndicatorState.Active => ShellStatusKind.Active,
            ShellIndicatorState.Warning => ShellStatusKind.Warning,
            ShellIndicatorState.Error => ShellStatusKind.Error,
            _ => ShellStatusKind.Offline
        };
        AutomationProperties.SetName(this, $"{State}: {Label}");
    }

    private void SyncLabel()
    {
        InnerBadge.Label = Label ?? string.Empty;
        AutomationProperties.SetName(this, $"{State}: {Label}");
    }
}
