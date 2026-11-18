using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

public sealed partial class SearchBarControl : UserControl
{
    public SearchBarControl()
    {
        InitializeComponent();
    }

    public event EventHandler<string>? QueryChanged;
    public event EventHandler<string>? QuerySubmitted;

    public string Placeholder
    {
        get => SearchBox.PlaceholderText;
        set => SearchBox.PlaceholderText = value ?? string.Empty;
    }

    public string QueryText
    {
        get => SearchBox.Text;
        set => SearchBox.Text = value ?? string.Empty;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            QueryChanged?.Invoke(this, sender.Text);
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        QuerySubmitted?.Invoke(this, args.QueryText ?? sender.Text);
    }
}
