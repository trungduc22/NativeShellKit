using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace NativeShellKit.DesignSystem.Controls;

public partial class CompactMediaBar : Control
{
    public static readonly DependencyProperty LayoutModeProperty = DependencyProperty.Register(
        nameof(LayoutMode), typeof(MediaBarLayout), typeof(CompactMediaBar),
        new PropertyMetadata(MediaBarLayout.Card, OnLayoutChanged));

    public static readonly DependencyProperty ConnectionStateProperty = DependencyProperty.Register(
        nameof(ConnectionState), typeof(ShellStatusKind), typeof(CompactMediaBar),
        new PropertyMetadata(ShellStatusKind.Offline));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ArtistProperty = DependencyProperty.Register(
        nameof(Artist), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ProviderLabelProperty = DependencyProperty.Register(
        nameof(ProviderLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ArtworkSourceProperty = DependencyProperty.Register(
        nameof(ArtworkSource), typeof(ImageSource), typeof(CompactMediaBar),
        new PropertyMetadata(null, OnArtworkChanged));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(CompactMediaBar),
        new PropertyMetadata(false, OnPlaybackStateChanged));

    public static readonly DependencyProperty PlayPauseGlyphProperty = DependencyProperty.Register(
        nameof(PlayPauseGlyph), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("\uE768"));

    public static readonly DependencyProperty PlayPauseAccessibleNameProperty = DependencyProperty.Register(
        nameof(PlayPauseAccessibleName), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("Play"));

    public static readonly DependencyProperty PositionSecondsProperty = DependencyProperty.Register(
        nameof(PositionSeconds), typeof(double), typeof(CompactMediaBar),
        new PropertyMetadata(0.0, OnTimeChanged));

    public static readonly DependencyProperty DurationSecondsProperty = DependencyProperty.Register(
        nameof(DurationSeconds), typeof(double), typeof(CompactMediaBar),
        new PropertyMetadata(0.0, OnTimeChanged));

    public static readonly DependencyProperty TimeDisplayTextProperty = DependencyProperty.Register(
        nameof(TimeDisplayText), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("00:00 / 00:00"));

    public static readonly DependencyProperty CanSeekProperty = DependencyProperty.Register(
        nameof(CanSeek), typeof(bool), typeof(CompactMediaBar),
        new PropertyMetadata(true));

    public static readonly DependencyProperty RouteLabelProperty = DependencyProperty.Register(
        nameof(RouteLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("PC"));

    public static readonly DependencyProperty VolumePercentProperty = DependencyProperty.Register(
        nameof(VolumePercent), typeof(double), typeof(CompactMediaBar),
        new PropertyMetadata(100.0));

    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(
        nameof(IsMuted), typeof(bool), typeof(CompactMediaBar),
        new PropertyMetadata(false, OnMuteChanged));

    public static readonly DependencyProperty MuteGlyphProperty = DependencyProperty.Register(
        nameof(MuteGlyph), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("\uE767"));

    public static readonly DependencyProperty PlayLabelProperty = DependencyProperty.Register(
        nameof(PlayLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("Play", OnPlaybackStateChanged));

    public static readonly DependencyProperty PauseLabelProperty = DependencyProperty.Register(
        nameof(PauseLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("Pause", OnPlaybackStateChanged));

    public static readonly DependencyProperty PreviousLabelProperty = DependencyProperty.Register(
        nameof(PreviousLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("Previous"));

    public static readonly DependencyProperty NextLabelProperty = DependencyProperty.Register(
        nameof(NextLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("Next"));

    public static readonly DependencyProperty SeekSliderLabelProperty = DependencyProperty.Register(
        nameof(SeekSliderLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("Position"));

    public static readonly DependencyProperty HandoffLabelProperty = DependencyProperty.Register(
        nameof(HandoffLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("Handoff", OnRouteActionsChanged));

    public static readonly DependencyProperty ReturnToPcLabelProperty = DependencyProperty.Register(
        nameof(ReturnToPcLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("PC", OnRouteActionsChanged));

    public static readonly DependencyProperty VolumeLabelProperty = DependencyProperty.Register(
        nameof(VolumeLabel), typeof(string), typeof(CompactMediaBar),
        new PropertyMetadata("Volume"));

    public static readonly DependencyProperty PlayPauseCommandProperty = DependencyProperty.Register(
        nameof(PlayPauseCommand), typeof(ICommand), typeof(CompactMediaBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PreviousCommandProperty = DependencyProperty.Register(
        nameof(PreviousCommand), typeof(ICommand), typeof(CompactMediaBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty NextCommandProperty = DependencyProperty.Register(
        nameof(NextCommand), typeof(ICommand), typeof(CompactMediaBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(CompactMediaBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty HandoffCommandProperty = DependencyProperty.Register(
        nameof(HandoffCommand), typeof(ICommand), typeof(CompactMediaBar),
        new PropertyMetadata(null, OnRouteActionsChanged));

    public static readonly DependencyProperty ReturnToPcCommandProperty = DependencyProperty.Register(
        nameof(ReturnToPcCommand), typeof(ICommand), typeof(CompactMediaBar),
        new PropertyMetadata(null, OnRouteActionsChanged));

    public static readonly DependencyProperty ToggleMuteCommandProperty = DependencyProperty.Register(
        nameof(ToggleMuteCommand), typeof(ICommand), typeof(CompactMediaBar),
        new PropertyMetadata(null));

    private Slider? _seekSlider;
    private Button? _handoffBtn;
    private Button? _returnBtn;
    private bool _syncingSlider;

    public CompactMediaBar()
    {
        DefaultStyleKey = typeof(CompactMediaBar);
        Unloaded += (_, _) => UnhookParts();
    }

    public MediaBarLayout LayoutMode { get => (MediaBarLayout)GetValue(LayoutModeProperty); set => SetValue(LayoutModeProperty, value); }
    public ShellStatusKind ConnectionState { get => (ShellStatusKind)GetValue(ConnectionStateProperty); set => SetValue(ConnectionStateProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Artist { get => (string)GetValue(ArtistProperty); set => SetValue(ArtistProperty, value); }
    public string ProviderLabel { get => (string)GetValue(ProviderLabelProperty); set => SetValue(ProviderLabelProperty, value); }
    public ImageSource? ArtworkSource { get => (ImageSource?)GetValue(ArtworkSourceProperty); set => SetValue(ArtworkSourceProperty, value); }
    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public string PlayPauseGlyph { get => (string)GetValue(PlayPauseGlyphProperty); private set => SetValue(PlayPauseGlyphProperty, value); }
    public string PlayPauseAccessibleName { get => (string)GetValue(PlayPauseAccessibleNameProperty); private set => SetValue(PlayPauseAccessibleNameProperty, value); }
    public double PositionSeconds { get => (double)GetValue(PositionSecondsProperty); set => SetValue(PositionSecondsProperty, value); }
    public double DurationSeconds { get => (double)GetValue(DurationSecondsProperty); set => SetValue(DurationSecondsProperty, value); }
    public string TimeDisplayText { get => (string)GetValue(TimeDisplayTextProperty); private set => SetValue(TimeDisplayTextProperty, value); }
    public bool CanSeek { get => (bool)GetValue(CanSeekProperty); set => SetValue(CanSeekProperty, value); }
    public string RouteLabel { get => (string)GetValue(RouteLabelProperty); set => SetValue(RouteLabelProperty, value); }
    public double VolumePercent { get => (double)GetValue(VolumePercentProperty); set => SetValue(VolumePercentProperty, value); }
    public bool IsMuted { get => (bool)GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }
    public string MuteGlyph { get => (string)GetValue(MuteGlyphProperty); private set => SetValue(MuteGlyphProperty, value); }
    public string PlayLabel { get => (string)GetValue(PlayLabelProperty); set => SetValue(PlayLabelProperty, value); }
    public string PauseLabel { get => (string)GetValue(PauseLabelProperty); set => SetValue(PauseLabelProperty, value); }
    public string PreviousLabel { get => (string)GetValue(PreviousLabelProperty); set => SetValue(PreviousLabelProperty, value); }
    public string NextLabel { get => (string)GetValue(NextLabelProperty); set => SetValue(NextLabelProperty, value); }
    public string SeekSliderLabel { get => (string)GetValue(SeekSliderLabelProperty); set => SetValue(SeekSliderLabelProperty, value); }
    public string HandoffLabel { get => (string)GetValue(HandoffLabelProperty); set => SetValue(HandoffLabelProperty, value); }
    public string ReturnToPcLabel { get => (string)GetValue(ReturnToPcLabelProperty); set => SetValue(ReturnToPcLabelProperty, value); }
    public string VolumeLabel { get => (string)GetValue(VolumeLabelProperty); set => SetValue(VolumeLabelProperty, value); }
    public ICommand? PlayPauseCommand { get => (ICommand?)GetValue(PlayPauseCommandProperty); set => SetValue(PlayPauseCommandProperty, value); }
    public ICommand? PreviousCommand { get => (ICommand?)GetValue(PreviousCommandProperty); set => SetValue(PreviousCommandProperty, value); }
    public ICommand? NextCommand { get => (ICommand?)GetValue(NextCommandProperty); set => SetValue(NextCommandProperty, value); }
    public ICommand? SeekCommand { get => (ICommand?)GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }
    public ICommand? HandoffCommand { get => (ICommand?)GetValue(HandoffCommandProperty); set => SetValue(HandoffCommandProperty, value); }
    public ICommand? ReturnToPcCommand { get => (ICommand?)GetValue(ReturnToPcCommandProperty); set => SetValue(ReturnToPcCommandProperty, value); }
    public ICommand? ToggleMuteCommand { get => (ICommand?)GetValue(ToggleMuteCommandProperty); set => SetValue(ToggleMuteCommandProperty, value); }

    protected override void OnApplyTemplate()
    {
        UnhookParts();
        base.OnApplyTemplate();

        _seekSlider = GetTemplateChild("PART_SeekSlider") as Slider;
        _handoffBtn = GetTemplateChild("PART_HandoffButton") as Button;
        _returnBtn = GetTemplateChild("PART_ReturnToPcButton") as Button;

        if (_seekSlider is not null)
            _seekSlider.ValueChanged += OnSliderValueChanged;

        UpdateLayoutVisualState(false);
        UpdatePlaybackProperties();
        UpdateMuteProperties();
        UpdateTimeProperties();
        UpdateArtworkVisualState();
        UpdateRouteButtonVisibility();
    }

    private void UnhookParts()
    {
        if (_seekSlider is not null)
        {
            _seekSlider.ValueChanged -= OnSliderValueChanged;
            _seekSlider = null;
        }
        _handoffBtn = null;
        _returnBtn = null;
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CompactMediaBar)d).UpdateLayoutVisualState(true);

    private static void OnArtworkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CompactMediaBar)d).UpdateArtworkVisualState();

    private static void OnPlaybackStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CompactMediaBar)d).UpdatePlaybackProperties();

    private static void OnMuteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CompactMediaBar)d).UpdateMuteProperties();

    private static void OnTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CompactMediaBar)d).UpdateTimeProperties();

    private static void OnRouteActionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CompactMediaBar)d).UpdateRouteButtonVisibility();

    private void UpdateLayoutVisualState(bool useTransitions)
    {
        VisualStateManager.GoToState(this, LayoutMode == MediaBarLayout.Strip ? "StripLayout" : "CardLayout", useTransitions);
    }

    private void UpdateArtworkVisualState()
    {
        VisualStateManager.GoToState(this, ArtworkSource is not null ? "ArtworkImage" : "ArtworkFallback", false);
    }

    private void UpdatePlaybackProperties()
    {
        PlayPauseGlyph = IsPlaying ? "\uE769" : "\uE768";
        PlayPauseAccessibleName = IsPlaying ? PauseLabel : PlayLabel;
    }

    private void UpdateMuteProperties()
    {
        MuteGlyph = IsMuted ? "\uE74F" : "\uE767";
    }

    private void UpdateRouteButtonVisibility()
    {
        if (_handoffBtn is not null)
            _handoffBtn.Visibility = HandoffCommand is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_returnBtn is not null)
            _returnBtn.Visibility = ReturnToPcCommand is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTimeProperties()
    {
        TimeDisplayText = $"{FormatSeconds(PositionSeconds)} / {FormatSeconds(DurationSeconds)}";
        if (_seekSlider is not null)
        {
            _syncingSlider = true;
            try
            {
                _seekSlider.Maximum = DurationSeconds > 0 ? DurationSeconds : 1.0;
                _seekSlider.Value = Math.Clamp(PositionSeconds, 0, _seekSlider.Maximum);
            }
            finally
            {
                _syncingSlider = false;
            }
        }
    }

    private void OnSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSlider)
            return;

        PositionSeconds = e.NewValue;
        var cmd = SeekCommand;
        if (cmd is not null && cmd.CanExecute(e.NewValue))
            cmd.Execute(e.NewValue);
    }

    private static string FormatSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            return "00:00";

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }
}
