using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NativeShellKit.DesignSystem.Controls;

public partial class ProgressBanner : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ProgressBanner),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusCaptionProperty = DependencyProperty.Register(
        nameof(StatusCaption), typeof(string), typeof(ProgressBanner),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ProgressPercentProperty = DependencyProperty.Register(
        nameof(ProgressPercent), typeof(double?), typeof(ProgressBanner),
        new PropertyMetadata(null, OnProgressChanged));

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(ProgressBanner),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ProgressValueProperty = DependencyProperty.Register(
        nameof(ProgressValue), typeof(double), typeof(ProgressBanner),
        new PropertyMetadata(0.0));

    public static readonly DependencyProperty IsPausedProperty = DependencyProperty.Register(
        nameof(IsPaused), typeof(bool), typeof(ProgressBanner),
        new PropertyMetadata(false));

    public static readonly DependencyProperty PauseResumeLabelProperty = DependencyProperty.Register(
        nameof(PauseResumeLabel), typeof(string), typeof(ProgressBanner),
        new PropertyMetadata(null, OnButtonsChanged));

    public static readonly DependencyProperty CancelLabelProperty = DependencyProperty.Register(
        nameof(CancelLabel), typeof(string), typeof(ProgressBanner),
        new PropertyMetadata(null, OnButtonsChanged));

    public static readonly DependencyProperty PauseResumeCommandProperty = DependencyProperty.Register(
        nameof(PauseResumeCommand), typeof(ICommand), typeof(ProgressBanner),
        new PropertyMetadata(null, OnButtonsChanged));

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
        nameof(CancelCommand), typeof(ICommand), typeof(ProgressBanner),
        new PropertyMetadata(null, OnButtonsChanged));

    private Button? _pauseBtn;
    private Button? _cancelBtn;

    public ProgressBanner()
    {
        DefaultStyleKey = typeof(ProgressBanner);
    }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string StatusCaption { get => (string)GetValue(StatusCaptionProperty); set => SetValue(StatusCaptionProperty, value); }
    public double? ProgressPercent { get => (double?)GetValue(ProgressPercentProperty); set => SetValue(ProgressPercentProperty, value); }
    public bool IsIndeterminate { get => (bool)GetValue(IsIndeterminateProperty); private set => SetValue(IsIndeterminateProperty, value); }
    public double ProgressValue { get => (double)GetValue(ProgressValueProperty); private set => SetValue(ProgressValueProperty, value); }
    public bool IsPaused { get => (bool)GetValue(IsPausedProperty); set => SetValue(IsPausedProperty, value); }
    public string? PauseResumeLabel { get => (string?)GetValue(PauseResumeLabelProperty); set => SetValue(PauseResumeLabelProperty, value); }
    public string? CancelLabel { get => (string?)GetValue(CancelLabelProperty); set => SetValue(CancelLabelProperty, value); }
    public ICommand? PauseResumeCommand { get => (ICommand?)GetValue(PauseResumeCommandProperty); set => SetValue(PauseResumeCommandProperty, value); }
    public ICommand? CancelCommand { get => (ICommand?)GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _pauseBtn = GetTemplateChild("PART_PauseButton") as Button;
        _cancelBtn = GetTemplateChild("PART_CancelButton") as Button;
        SyncProgress();
        SyncButtons();
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ProgressBanner)d).SyncProgress();

    private static void OnButtonsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ProgressBanner)d).SyncButtons();

    private void SyncProgress()
    {
        IsIndeterminate = !ProgressPercent.HasValue;
        ProgressValue = ProgressPercent.HasValue ? Math.Clamp(ProgressPercent.Value, 0, 100) : 0;
    }

    private void SyncButtons()
    {
        if (_pauseBtn is not null)
            _pauseBtn.Visibility = PauseResumeCommand is not null && !string.IsNullOrWhiteSpace(PauseResumeLabel)
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (_cancelBtn is not null)
            _cancelBtn.Visibility = CancelCommand is not null && !string.IsNullOrWhiteSpace(CancelLabel)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }
}

public partial class InlineProgressIndicator : Control
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(InlineProgressIndicator),
        new PropertyMetadata(true, (d, _) => ((InlineProgressIndicator)d).UpdateState()));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(InlineProgressIndicator),
        new PropertyMetadata(string.Empty));

    public InlineProgressIndicator()
    {
        DefaultStyleKey = typeof(InlineProgressIndicator);
        Loaded += (_, _) => UpdateState();
    }

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateState();
    }

    private void UpdateState()
    {
        VisualStateManager.GoToState(this, IsActive ? "Active" : "Idle", false);
    }
}
