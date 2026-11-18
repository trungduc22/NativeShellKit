using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

public sealed partial class CompactMetricCard : UserControl
{
    public CompactMetricCard()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => LabelTextBlock.Text;
        set
        {
            LabelTextBlock.Text = value ?? string.Empty;
            UpdateAutomationName();
        }
    }

    public string Value
    {
        get => ValueTextBlock.Text;
        set
        {
            ValueTextBlock.Text = value ?? string.Empty;
            UpdateAutomationName();
        }
    }

    public string? Caption
    {
        get => CaptionTextBlock.Text;
        set
        {
            CaptionTextBlock.Text = value ?? string.Empty;
            CaptionTextBlock.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public string Glyph
    {
        get => CardIcon.Glyph;
        set => CardIcon.Glyph = string.IsNullOrWhiteSpace(value) ? "\uE9D9" : value;
    }

    private void UpdateAutomationName()
    {
        AutomationProperties.SetName(this, $"{LabelTextBlock.Text}: {ValueTextBlock.Text}");
    }
}
