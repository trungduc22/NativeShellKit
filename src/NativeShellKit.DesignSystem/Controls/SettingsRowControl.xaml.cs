using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

public sealed partial class SettingsRowControl : UserControl
{
    public SettingsRowControl()
    {
        InitializeComponent();
    }

    public string Header
    {
        get => HeaderTextBlock.Text;
        set
        {
            HeaderTextBlock.Text = value ?? string.Empty;
            AutomationProperties.SetName(this, HeaderTextBlock.Text);
        }
    }

    public string? Description
    {
        get => DescriptionTextBlock.Text;
        set
        {
            DescriptionTextBlock.Text = value ?? string.Empty;
            DescriptionTextBlock.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public string? Glyph
    {
        get => RowIcon.Glyph;
        set
        {
            RowIcon.Glyph = value ?? string.Empty;
            RowIcon.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object? ActionContent
    {
        get => TrailingPresenter.Content;
        set => TrailingPresenter.Content = value;
    }
}
