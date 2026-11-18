using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NativeShellKit.DesignSystem.Controls;

public enum ShellIndicatorState
{
    Healthy,
    Active,
    Warning,
    Error,
    Offline
}

public sealed partial class StatusIndicatorControl : UserControl
{
    private ShellIndicatorState _state = ShellIndicatorState.Healthy;

    public StatusIndicatorControl()
    {
        InitializeComponent();
        ApplyVisualState();
    }

    public ShellIndicatorState State
    {
        get => _state;
        set
        {
            _state = value;
            ApplyVisualState();
        }
    }

    public string Label
    {
        get => LabelTextBlock.Text;
        set
        {
            LabelTextBlock.Text = value ?? string.Empty;
            AutomationProperties.SetName(this, $"{_state}: {LabelTextBlock.Text}");
        }
    }

    private void ApplyVisualState()
    {
        var (bg, fg) = _state switch
        {
            ShellIndicatorState.Healthy => (Color.FromArgb(32, 16, 185, 129), Color.FromArgb(255, 5, 150, 105)),
            ShellIndicatorState.Active => (Color.FromArgb(32, 37, 99, 235), Color.FromArgb(255, 37, 99, 235)),
            ShellIndicatorState.Warning => (Color.FromArgb(32, 217, 119, 6), Color.FromArgb(255, 217, 119, 6)),
            ShellIndicatorState.Error => (Color.FromArgb(32, 220, 38, 38), Color.FromArgb(255, 220, 38, 38)),
            _ => (Color.FromArgb(32, 100, 116, 139), Color.FromArgb(255, 100, 116, 139))
        };

        var fgBrush = new SolidColorBrush(fg);
        PillBorder.Background = new SolidColorBrush(bg);
        DotEllipse.Fill = fgBrush;
        LabelTextBlock.Foreground = fgBrush;
    }
}
