using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

public enum ShellViewState
{
    Content,
    Loading,
    Empty,
    Error
}

public sealed partial class StateViewControl : UserControl
{
    public StateViewControl()
    {
        InitializeComponent();
    }

    public event EventHandler? RetryRequested;

    public object? BodyContent
    {
        get => MainContentPresenter.Content;
        set => MainContentPresenter.Content = value;
    }

    public void SetState(ShellViewState state, string title = "", string message = "")
    {
        if (state == ShellViewState.Content)
        {
            MainContentPresenter.Visibility = Visibility.Visible;
            OverlayPanel.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = false;
            return;
        }

        MainContentPresenter.Visibility = Visibility.Collapsed;
        OverlayPanel.Visibility = Visibility.Visible;
        StateTitleText.Text = title;
        StateMessageText.Text = message;

        LoadingRing.Visibility = state == ShellViewState.Loading ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = state == ShellViewState.Loading;
        StateIcon.Visibility = state != ShellViewState.Loading ? Visibility.Visible : Visibility.Collapsed;
        StateIcon.Glyph = state == ShellViewState.Error ? "\uEA39" : "\uE8F4";
        RetryButton.Visibility = state == ShellViewState.Error ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        RetryRequested?.Invoke(this, EventArgs.Empty);
    }
}
