using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

public sealed partial class SettingsSectionControl : UserControl
{
    public SettingsSectionControl()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => SectionTitleText.Text;
        set => SectionTitleText.Text = value ?? string.Empty;
    }

    public string? Description
    {
        get => SectionDescriptionText.Text;
        set
        {
            SectionDescriptionText.Text = value ?? string.Empty;
            SectionDescriptionText.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public UIElementCollection Items => RowsHostPanel.Children;

    public void AddRow(UIElement row)
    {
        ArgumentNullException.ThrowIfNull(row);
        RowsHostPanel.Children.Add(row);
    }
}
