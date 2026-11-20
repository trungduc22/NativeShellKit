using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using NativeShellKit.Abstractions.Modules;

namespace NativeShellKit.DesignSystem.Controls;

[ContentProperty(Name = nameof(ModuleContent))]
public partial class ModuleCardBoundary : Control
{
    public static readonly DependencyProperty ModuleIdProperty = DependencyProperty.Register(
        nameof(ModuleId), typeof(string), typeof(ModuleCardBoundary),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ModuleTitleProperty = DependencyProperty.Register(
        nameof(ModuleTitle), typeof(string), typeof(ModuleCardBoundary),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HealthStateProperty = DependencyProperty.Register(
        nameof(HealthState), typeof(ModuleHealthState), typeof(ModuleCardBoundary),
        new PropertyMetadata(ModuleHealthState.Healthy, OnHealthChanged));

    public static readonly DependencyProperty StatusMessageProperty = DependencyProperty.Register(
        nameof(StatusMessage), typeof(string), typeof(ModuleCardBoundary),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ModuleContentProperty = DependencyProperty.Register(
        nameof(ModuleContent), typeof(object), typeof(ModuleCardBoundary),
        new PropertyMetadata(null));

    public static readonly DependencyProperty DisabledTitleProperty = DependencyProperty.Register(
        nameof(DisabledTitle), typeof(string), typeof(ModuleCardBoundary),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DegradedTitleProperty = DependencyProperty.Register(
        nameof(DegradedTitle), typeof(string), typeof(ModuleCardBoundary),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FaultedTitleProperty = DependencyProperty.Register(
        nameof(FaultedTitle), typeof(string), typeof(ModuleCardBoundary),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RetryLabelProperty = DependencyProperty.Register(
        nameof(RetryLabel), typeof(string), typeof(ModuleCardBoundary),
        new PropertyMetadata(null, OnHealthChanged));

    public static readonly DependencyProperty ConfigureLabelProperty = DependencyProperty.Register(
        nameof(ConfigureLabel), typeof(string), typeof(ModuleCardBoundary),
        new PropertyMetadata(null, OnHealthChanged));

    public static readonly DependencyProperty RetryCommandProperty = DependencyProperty.Register(
        nameof(RetryCommand), typeof(ICommand), typeof(ModuleCardBoundary),
        new PropertyMetadata(null, OnHealthChanged));

    public static readonly DependencyProperty ConfigureCommandProperty = DependencyProperty.Register(
        nameof(ConfigureCommand), typeof(ICommand), typeof(ModuleCardBoundary),
        new PropertyMetadata(null, OnHealthChanged));

    public static readonly DependencyProperty FallbackHeadingProperty = DependencyProperty.Register(
        nameof(FallbackHeading), typeof(string), typeof(ModuleCardBoundary),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FallbackViewStateProperty = DependencyProperty.Register(
        nameof(FallbackViewState), typeof(ShellViewState), typeof(ModuleCardBoundary),
        new PropertyMetadata(ShellViewState.Content));

    public ModuleCardBoundary()
    {
        DefaultStyleKey = typeof(ModuleCardBoundary);
        Loaded += (_, _) => UpdateState(false);
    }

    public string ModuleId { get => (string)GetValue(ModuleIdProperty); set => SetValue(ModuleIdProperty, value); }
    public string ModuleTitle { get => (string)GetValue(ModuleTitleProperty); set => SetValue(ModuleTitleProperty, value); }
    public ModuleHealthState HealthState { get => (ModuleHealthState)GetValue(HealthStateProperty); set => SetValue(HealthStateProperty, value); }
    public string StatusMessage { get => (string)GetValue(StatusMessageProperty); set => SetValue(StatusMessageProperty, value); }
    public object? ModuleContent { get => GetValue(ModuleContentProperty); set => SetValue(ModuleContentProperty, value); }
    public string DisabledTitle { get => (string)GetValue(DisabledTitleProperty); set => SetValue(DisabledTitleProperty, value); }
    public string DegradedTitle { get => (string)GetValue(DegradedTitleProperty); set => SetValue(DegradedTitleProperty, value); }
    public string FaultedTitle { get => (string)GetValue(FaultedTitleProperty); set => SetValue(FaultedTitleProperty, value); }
    public string? RetryLabel { get => (string?)GetValue(RetryLabelProperty); set => SetValue(RetryLabelProperty, value); }
    public string? ConfigureLabel { get => (string?)GetValue(ConfigureLabelProperty); set => SetValue(ConfigureLabelProperty, value); }
    public ICommand? RetryCommand { get => (ICommand?)GetValue(RetryCommandProperty); set => SetValue(RetryCommandProperty, value); }
    public ICommand? ConfigureCommand { get => (ICommand?)GetValue(ConfigureCommandProperty); set => SetValue(ConfigureCommandProperty, value); }
    public string FallbackHeading { get => (string)GetValue(FallbackHeadingProperty); private set => SetValue(FallbackHeadingProperty, value); }
    public ShellViewState FallbackViewState { get => (ShellViewState)GetValue(FallbackViewStateProperty); private set => SetValue(FallbackViewStateProperty, value); }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateState(false);
    }

    private static void OnHealthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ModuleCardBoundary)d).UpdateState(true);

    private void UpdateState(bool useTransitions)
    {
        switch (HealthState)
        {
            case ModuleHealthState.Healthy:
            case ModuleHealthState.Uninitialized:
            case ModuleHealthState.Starting:
            case ModuleHealthState.Stopped:
                FallbackViewState = ShellViewState.Content;
                FallbackHeading = ModuleTitle;
                VisualStateManager.GoToState(this, "HealthyState", useTransitions);
                break;

            case ModuleHealthState.Disabled:
                FallbackViewState = ShellViewState.Empty;
                FallbackHeading = !string.IsNullOrWhiteSpace(DisabledTitle) ? DisabledTitle : ModuleTitle;
                VisualStateManager.GoToState(this, "FallbackState", useTransitions);
                break;

            case ModuleHealthState.Degraded:
                FallbackViewState = ShellViewState.Degraded;
                FallbackHeading = !string.IsNullOrWhiteSpace(DegradedTitle) ? DegradedTitle : ModuleTitle;
                VisualStateManager.GoToState(this, "FallbackState", useTransitions);
                break;

            default: // Faulted
                FallbackViewState = ShellViewState.Error;
                FallbackHeading = !string.IsNullOrWhiteSpace(FaultedTitle) ? FaultedTitle : ModuleTitle;
                VisualStateManager.GoToState(this, "FallbackState", useTransitions);
                break;
        }
    }
}
