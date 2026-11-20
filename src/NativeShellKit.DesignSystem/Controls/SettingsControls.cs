using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace NativeShellKit.DesignSystem.Controls;

[ContentProperty(Name = nameof(Items))]
public partial class SettingsSection : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingsSection),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsSection),
        new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty HeaderActionContentProperty = DependencyProperty.Register(
        nameof(HeaderActionContent), typeof(object), typeof(SettingsSection),
        new PropertyMetadata(null));

    private StackPanel? _rowsHost;

    public SettingsSection()
    {
        DefaultStyleKey = typeof(SettingsSection);
        Items = new ObservableCollection<UIElement>();
        Items.CollectionChanged += (_, _) => SyncRows();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? HeaderActionContent
    {
        get => GetValue(HeaderActionContentProperty);
        set => SetValue(HeaderActionContentProperty, value);
    }

    public ObservableCollection<UIElement> Items { get; }

    public void AddRow(UIElement row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Items.Add(row);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _rowsHost = GetTemplateChild("PART_RowsHost") as StackPanel;
        SyncRows();
        UpdateDescriptionState();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SettingsSection)d).UpdateDescriptionState();
    }

    private void UpdateDescriptionState()
    {
        VisualStateManager.GoToState(
            this,
            string.IsNullOrWhiteSpace(Description) ? "DescriptionCollapsed" : "DescriptionVisible",
            false);
    }

    private void SyncRows()
    {
        if (_rowsHost is null)
            return;

        _rowsHost.Children.Clear();
        foreach (var item in Items)
            _rowsHost.Children.Add(item);
    }
}

public sealed class SettingsSectionControl : SettingsSection
{
}

[ContentProperty(Name = nameof(ActionContent))]
public partial class SettingsRow : Control
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsRow),
        new PropertyMetadata(string.Empty, OnHeaderChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsRow),
        new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsRow),
        new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(SettingsRow),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ValidationMessageProperty = DependencyProperty.Register(
        nameof(ValidationMessage), typeof(string), typeof(SettingsRow),
        new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty IsExpandableProperty = DependencyProperty.Register(
        nameof(IsExpandable), typeof(bool), typeof(SettingsRow),
        new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(SettingsRow),
        new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty ExpandedContentProperty = DependencyProperty.Register(
        nameof(ExpandedContent), typeof(object), typeof(SettingsRow),
        new PropertyMetadata(null));

    private Button? _expandToggleButton;

    public SettingsRow()
    {
        DefaultStyleKey = typeof(SettingsRow);
        Unloaded += (_, _) => UnhookExpandToggle();
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public string? ValidationMessage
    {
        get => (string?)GetValue(ValidationMessageProperty);
        set => SetValue(ValidationMessageProperty, value);
    }

    public bool IsExpandable
    {
        get => (bool)GetValue(IsExpandableProperty);
        set => SetValue(IsExpandableProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public object? ExpandedContent
    {
        get => GetValue(ExpandedContentProperty);
        set => SetValue(ExpandedContentProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        UnhookExpandToggle();
        base.OnApplyTemplate();
        _expandToggleButton = GetTemplateChild("PART_ExpandButton") as Button;
        if (_expandToggleButton is not null)
            _expandToggleButton.Click += OnExpandClicked;

        UpdateAutomation();
        UpdateVisualStates(false);
    }

    private void UnhookExpandToggle()
    {
        if (_expandToggleButton is not null)
        {
            _expandToggleButton.Click -= OnExpandClicked;
            _expandToggleButton = null;
        }
    }

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SettingsRow)d).UpdateAutomation();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SettingsRow)d).UpdateVisualStates(true);
    }

    private void UpdateAutomation()
    {
        if (string.IsNullOrEmpty(AutomationProperties.GetName(this)))
            AutomationProperties.SetName(this, Header ?? string.Empty);
    }

    private void UpdateVisualStates(bool useTransitions)
    {
        VisualStateManager.GoToState(this, string.IsNullOrWhiteSpace(Description) ? "DescriptionCollapsed" : "DescriptionVisible", useTransitions);
        VisualStateManager.GoToState(this, string.IsNullOrWhiteSpace(Glyph) ? "GlyphCollapsed" : "GlyphVisible", useTransitions);
        VisualStateManager.GoToState(this, string.IsNullOrWhiteSpace(ValidationMessage) ? "ValidationCollapsed" : "ValidationVisible", useTransitions);
        VisualStateManager.GoToState(this, IsExpandable ? "Expandable" : "StandardRow", useTransitions);
        VisualStateManager.GoToState(this, IsExpandable && IsExpanded ? "Expanded" : "Collapsed", useTransitions);
    }

    private void OnExpandClicked(object sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
    }
}

public sealed class SettingsRowControl : SettingsRow
{
}
