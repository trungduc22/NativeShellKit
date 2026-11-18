using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

public sealed partial class PageHeaderControl : UserControl
{
    public PageHeaderControl()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleTextBlock.Text;
        set => TitleTextBlock.Text = value ?? string.Empty;
    }

    public string? Subtitle
    {
        get => SubtitleTextBlock.Text;
        set
        {
            SubtitleTextBlock.Text = value ?? string.Empty;
            SubtitleTextBlock.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public string? BadgeText
    {
        get => BadgeTextBlock.Text;
        set
        {
            BadgeTextBlock.Text = value ?? string.Empty;
            BadgeContainer.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object? ActionsContent
    {
        get => ActionsPresenter.Content;
        set => ActionsPresenter.Content = value;
    }
}
