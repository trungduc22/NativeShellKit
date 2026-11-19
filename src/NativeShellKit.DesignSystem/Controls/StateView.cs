using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace NativeShellKit.DesignSystem.Controls;

[ContentProperty(Name = nameof(BodyContent))]
public partial class StateView : Control
{
    public static readonly DependencyProperty BodyContentProperty = DependencyProperty.Register(
        nameof(BodyContent), typeof(object), typeof(StateView),
        new PropertyMetadata(null));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ShellViewState), typeof(StateView),
        new PropertyMetadata(ShellViewState.Content, OnVisualPropertyChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StateView),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(StateView),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconGlyphProperty = DependencyProperty.Register(
        nameof(IconGlyph), typeof(string), typeof(StateView),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty ResolvedGlyphProperty = DependencyProperty.Register(
        nameof(ResolvedGlyph), typeof(string), typeof(StateView),
        new PropertyMetadata("\uE8F4"));

    public static readonly DependencyProperty PrimaryActionLabelProperty = DependencyProperty.Register(
        nameof(PrimaryActionLabel), typeof(string), typeof(StateView),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty PrimaryActionCommandProperty = DependencyProperty.Register(
        nameof(PrimaryActionCommand), typeof(ICommand), typeof(StateView),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty SecondaryActionLabelProperty = DependencyProperty.Register(
        nameof(SecondaryActionLabel), typeof(string), typeof(StateView),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty SecondaryActionCommandProperty = DependencyProperty.Register(
        nameof(SecondaryActionCommand), typeof(ICommand), typeof(StateView),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty SkeletonTemplateProperty = DependencyProperty.Register(
        nameof(SkeletonTemplate), typeof(DataTemplate), typeof(StateView),
        new PropertyMetadata(null));

    private Button? _primaryBtn;
    private Button? _secondaryBtn;

    public StateView()
    {
        DefaultStyleKey = typeof(StateView);
        Unloaded += (_, _) => UnhookButtons();
    }

    public event EventHandler? RetryRequested;

    public object? BodyContent
    {
        get => GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    public ShellViewState State
    {
        get => (ShellViewState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? IconGlyph
    {
        get => (string?)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string ResolvedGlyph
    {
        get => (string)GetValue(ResolvedGlyphProperty);
        private set => SetValue(ResolvedGlyphProperty, value);
    }

    public string? PrimaryActionLabel
    {
        get => (string?)GetValue(PrimaryActionLabelProperty);
        set => SetValue(PrimaryActionLabelProperty, value);
    }

    public ICommand? PrimaryActionCommand
    {
        get => (ICommand?)GetValue(PrimaryActionCommandProperty);
        set => SetValue(PrimaryActionCommandProperty, value);
    }

    public string? SecondaryActionLabel
    {
        get => (string?)GetValue(SecondaryActionLabelProperty);
        set => SetValue(SecondaryActionLabelProperty, value);
    }

    public ICommand? SecondaryActionCommand
    {
        get => (ICommand?)GetValue(SecondaryActionCommandProperty);
        set => SetValue(SecondaryActionCommandProperty, value);
    }

    public DataTemplate? SkeletonTemplate
    {
        get => (DataTemplate?)GetValue(SkeletonTemplateProperty);
        set => SetValue(SkeletonTemplateProperty, value);
    }

    public void SetState(ShellViewState state, string title = "", string message = "")
    {
        State = state;
        Title = title;
        Message = message;
    }

    protected override void OnApplyTemplate()
    {
        UnhookButtons();
        base.OnApplyTemplate();

        _primaryBtn = GetTemplateChild("PART_PrimaryButton") as Button;
        _secondaryBtn = GetTemplateChild("PART_SecondaryButton") as Button;

        if (_primaryBtn is not null)
            _primaryBtn.Click += OnPrimaryClicked;
        if (_secondaryBtn is not null)
            _secondaryBtn.Click += OnSecondaryClicked;

        UpdateVisualStates(false);
    }

    private void UnhookButtons()
    {
        if (_primaryBtn is not null)
        {
            _primaryBtn.Click -= OnPrimaryClicked;
            _primaryBtn = null;
        }
        if (_secondaryBtn is not null)
        {
            _secondaryBtn.Click -= OnSecondaryClicked;
            _secondaryBtn = null;
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StateView)d).UpdateVisualStates(true);
    }

    private void UpdateVisualStates(bool useTransitions)
    {
        ResolvedGlyph = !string.IsNullOrWhiteSpace(IconGlyph)
            ? IconGlyph!
            : State switch
            {
                ShellViewState.Error => "\uEA39",
                ShellViewState.Offline => "\uF384",
                ShellViewState.Degraded => "\uE7BA",
                _ => "\uE8F4"
            };

        string viewState = State switch
        {
            ShellViewState.Content => "Content",
            ShellViewState.Loading => "Loading",
            ShellViewState.Skeleton => "Skeleton",
            ShellViewState.Empty => "Empty",
            ShellViewState.Error => "Error",
            ShellViewState.Offline => "Offline",
            ShellViewState.Degraded => "Degraded",
            _ => "Content"
        };
        VisualStateManager.GoToState(this, viewState, useTransitions);

        bool hasExplicitPrimary = !string.IsNullOrWhiteSpace(PrimaryActionLabel);
        bool showPrimary = hasExplicitPrimary || (State == ShellViewState.Error && RetryRequested is not null);
        if (_primaryBtn is not null)
        {
            _primaryBtn.Visibility = showPrimary ? Visibility.Visible : Visibility.Collapsed;
            if (!hasExplicitPrimary && showPrimary)
                _primaryBtn.Content = "Retry";
        }

        if (_secondaryBtn is not null)
        {
            _secondaryBtn.Visibility = !string.IsNullOrWhiteSpace(SecondaryActionLabel)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void OnPrimaryClicked(object sender, RoutedEventArgs e)
    {
        var cmd = PrimaryActionCommand;
        if (cmd is not null && cmd.CanExecute(null))
            cmd.Execute(null);
        else
            RetryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSecondaryClicked(object sender, RoutedEventArgs e)
    {
        var cmd = SecondaryActionCommand;
        if (cmd is not null && cmd.CanExecute(null))
            cmd.Execute(null);
    }
}

public sealed class StateViewControl : StateView
{
}
