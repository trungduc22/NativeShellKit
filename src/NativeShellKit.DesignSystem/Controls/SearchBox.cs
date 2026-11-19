using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NativeShellKit.DesignSystem.Controls;

public partial class SearchBox : Control
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchBox),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty QueryTextProperty = DependencyProperty.Register(
        nameof(QueryText), typeof(string), typeof(SearchBox),
        new PropertyMetadata(string.Empty, OnQueryTextChanged));

    public static readonly DependencyProperty ShortcutHintProperty = DependencyProperty.Register(
        nameof(ShortcutHint), typeof(string), typeof(SearchBox),
        new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty DebounceMillisecondsProperty = DependencyProperty.Register(
        nameof(DebounceMilliseconds), typeof(int), typeof(SearchBox),
        new PropertyMetadata(200));

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(object), typeof(SearchBox),
        new PropertyMetadata(null));

    public static readonly DependencyProperty QueryChangedCommandProperty = DependencyProperty.Register(
        nameof(QueryChangedCommand), typeof(ICommand), typeof(SearchBox),
        new PropertyMetadata(null));

    public static readonly DependencyProperty QuerySubmittedCommandProperty = DependencyProperty.Register(
        nameof(QuerySubmittedCommand), typeof(ICommand), typeof(SearchBox),
        new PropertyMetadata(null));

    public static readonly DependencyProperty AccessibleNameProperty = DependencyProperty.Register(
        nameof(AccessibleName), typeof(string), typeof(SearchBox),
        new PropertyMetadata("Search"));

    private AutoSuggestBox? _suggestBox;
    private CancellationTokenSource? _debounceCts;
    private bool _syncingText;
    private bool _focused;

    public SearchBox()
    {
        DefaultStyleKey = typeof(SearchBox);
        Unloaded += (_, _) => CancelAndDisposeDebounce();
    }

    public event EventHandler<string>? QueryChanged;
    public event EventHandler<string>? QuerySubmitted;

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string QueryText
    {
        get => (string)GetValue(QueryTextProperty);
        set => SetValue(QueryTextProperty, value);
    }

    public string? ShortcutHint
    {
        get => (string?)GetValue(ShortcutHintProperty);
        set => SetValue(ShortcutHintProperty, value);
    }

    public int DebounceMilliseconds
    {
        get => (int)GetValue(DebounceMillisecondsProperty);
        set => SetValue(DebounceMillisecondsProperty, value);
    }

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? QueryChangedCommand
    {
        get => (ICommand?)GetValue(QueryChangedCommandProperty);
        set => SetValue(QueryChangedCommandProperty, value);
    }

    public ICommand? QuerySubmittedCommand
    {
        get => (ICommand?)GetValue(QuerySubmittedCommandProperty);
        set => SetValue(QuerySubmittedCommandProperty, value);
    }

    public string AccessibleName
    {
        get => (string)GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value);
    }

    public void FocusAndSelectAll()
    {
        if (_suggestBox is null)
            return;

        _suggestBox.Focus(FocusState.Programmatic);
        var tb = FindDescendant<TextBox>(_suggestBox);
        tb?.SelectAll();
    }

    protected override void OnApplyTemplate()
    {
        UnhookSuggestBox();
        base.OnApplyTemplate();

        _suggestBox = GetTemplateChild("PART_AutoSuggestBox") as AutoSuggestBox;
        if (_suggestBox is not null)
        {
            _suggestBox.TextChanged += OnSuggestBoxTextChanged;
            _suggestBox.QuerySubmitted += OnSuggestBoxQuerySubmitted;
            _suggestBox.GotFocus += OnSuggestBoxGotFocus;
            _suggestBox.LostFocus += OnSuggestBoxLostFocus;
            _suggestBox.Text = QueryText ?? string.Empty;
        }

        UpdateShortcutHintVisibility();
    }

    private void UnhookSuggestBox()
    {
        if (_suggestBox is not null)
        {
            _suggestBox.TextChanged -= OnSuggestBoxTextChanged;
            _suggestBox.QuerySubmitted -= OnSuggestBoxQuerySubmitted;
            _suggestBox.GotFocus -= OnSuggestBoxGotFocus;
            _suggestBox.LostFocus -= OnSuggestBoxLostFocus;
            _suggestBox = null;
        }
        CancelAndDisposeDebounce();
    }

    private void CancelAndDisposeDebounce()
    {
        if (_debounceCts is not null)
        {
            try { _debounceCts.Cancel(); } catch { }
            _debounceCts.Dispose();
            _debounceCts = null;
        }
    }

    private static void OnQueryTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (SearchBox)d;
        if (ctl._suggestBox is not null && !ctl._syncingText)
        {
            ctl._syncingText = true;
            ctl._suggestBox.Text = e.NewValue as string ?? string.Empty;
            ctl._syncingText = false;
        }
        ctl.UpdateShortcutHintVisibility();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SearchBox)d).UpdateShortcutHintVisibility();
    }

    private void UpdateShortcutHintVisibility()
    {
        bool showHint = !_focused && string.IsNullOrEmpty(QueryText) && !string.IsNullOrWhiteSpace(ShortcutHint);
        VisualStateManager.GoToState(this, showHint ? "HintVisible" : "HintCollapsed", true);
    }

    private void OnSuggestBoxGotFocus(object sender, RoutedEventArgs e)
    {
        _focused = true;
        UpdateShortcutHintVisibility();
    }

    private void OnSuggestBoxLostFocus(object sender, RoutedEventArgs e)
    {
        _focused = false;
        UpdateShortcutHintVisibility();
    }

    private async void OnSuggestBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_syncingText)
            return;

        _syncingText = true;
        try
        {
            QueryText = sender.Text;
        }
        finally
        {
            _syncingText = false;
        }

        UpdateShortcutHintVisibility();
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        CancelAndDisposeDebounce();
        string captured = sender.Text;
        int delayMs = Math.Max(0, DebounceMilliseconds);
        if (delayMs == 0)
        {
            RaiseQueryChanged(captured);
            return;
        }

        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        try
        {
            await Task.Delay(delayMs, cts.Token).ConfigureAwait(true);
            if (!cts.IsCancellationRequested)
                RaiseQueryChanged(captured);
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer keystroke or unloaded.
        }
    }

    private void RaiseQueryChanged(string text)
    {
        QueryChanged?.Invoke(this, text);
        var cmd = QueryChangedCommand;
        if (cmd is not null && cmd.CanExecute(text))
            cmd.Execute(text);
    }

    private void OnSuggestBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        CancelAndDisposeDebounce();
        string text = args.QueryText ?? sender.Text;
        QuerySubmitted?.Invoke(this, text);
        var cmd = QuerySubmittedCommand;
        if (cmd is not null && cmd.CanExecute(text))
            cmd.Execute(text);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            return match;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }
        return null;
    }
}

public sealed class SearchBarControl : SearchBox
{
}
