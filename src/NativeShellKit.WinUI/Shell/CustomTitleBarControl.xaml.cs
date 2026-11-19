using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.WinUI.Shell;

[Obsolete("Use ShellTitleBar instead. CustomTitleBarControl will be removed in 0.3.0.")]
public sealed partial class CustomTitleBarControl : UserControl
{
    public CustomTitleBarControl()
    {
        InitializeComponent();
    }

    public event EventHandler? ThemeToggleRequested;

    public string TitleText
    {
        get => TitleTextBlock.Text;
        set => TitleTextBlock.Text = value ?? string.Empty;
    }

    public string? SubtitleText
    {
        get => SubtitleTextBlock.Text;
        set
        {
            SubtitleTextBlock.Text = value ?? string.Empty;
            SubtitleTextBlock.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object? CenterContent
    {
        get => CenterSlotPresenter.Content;
        set => CenterSlotPresenter.Content = value;
    }

    public object? RightContent
    {
        get => RightSlotPresenter.Content;
        set => RightSlotPresenter.Content = value;
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeToggleRequested?.Invoke(this, EventArgs.Empty);
    }
}
