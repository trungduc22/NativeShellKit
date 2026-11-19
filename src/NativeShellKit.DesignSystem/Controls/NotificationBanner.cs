using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.Notifications;

namespace NativeShellKit.DesignSystem.Controls;

public partial class NotificationBanner : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(NotificationBanner),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(NotificationBanner),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(InfoBarSeverity), typeof(NotificationBanner),
        new PropertyMetadata(InfoBarSeverity.Informational, OnSeverityChanged));

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(NotificationBanner),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ActionLabelProperty = DependencyProperty.Register(
        nameof(ActionLabel), typeof(string), typeof(NotificationBanner),
        new PropertyMetadata(null, OnActionChanged));

    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(NotificationBanner),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActionCommandParameterProperty = DependencyProperty.Register(
        nameof(ActionCommandParameter), typeof(object), typeof(NotificationBanner),
        new PropertyMetadata(null));

    private InfoBar? _infoBar;
    private Button? _actionButton;

    public NotificationBanner()
    {
        DefaultStyleKey = typeof(NotificationBanner);
        Unloaded += (_, _) => UnhookTemplateParts();
    }

    public event EventHandler? ActionInvoked;
    public event EventHandler? Dismissed;

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

    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string? ActionLabel
    {
        get => (string?)GetValue(ActionLabelProperty);
        set => SetValue(ActionLabelProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public object? ActionCommandParameter
    {
        get => GetValue(ActionCommandParameterProperty);
        set => SetValue(ActionCommandParameterProperty, value);
    }

    public void Show(NotificationRecord record, string? actionLabel = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        Title = record.Title;
        Message = record.Body;
        Severity = record.Severity switch
        {
            NotificationSeverity.Success => InfoBarSeverity.Success,
            NotificationSeverity.Warning => InfoBarSeverity.Warning,
            NotificationSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
        ActionLabel = actionLabel;
        IsOpen = true;
    }

    protected override void OnApplyTemplate()
    {
        UnhookTemplateParts();
        base.OnApplyTemplate();

        _infoBar = GetTemplateChild("PART_InfoBar") as InfoBar;
        _actionButton = GetTemplateChild("PART_ActionButton") as Button;

        if (_infoBar is not null)
            _infoBar.Closed += OnInfoBarClosed;
        if (_actionButton is not null)
            _actionButton.Click += OnActionClicked;

        SyncActionButton();
        UpdateLiveSetting();
    }

    private void UnhookTemplateParts()
    {
        if (_infoBar is not null)
        {
            _infoBar.Closed -= OnInfoBarClosed;
            _infoBar = null;
        }
        if (_actionButton is not null)
        {
            _actionButton.Click -= OnActionClicked;
            _actionButton = null;
        }
    }

    private static void OnActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((NotificationBanner)d).SyncActionButton();
    }

    private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((NotificationBanner)d).UpdateLiveSetting();
    }

    private void SyncActionButton()
    {
        if (_actionButton is not null)
            _actionButton.Visibility = string.IsNullOrWhiteSpace(ActionLabel) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateLiveSetting()
    {
        AutomationProperties.SetLiveSetting(
            this,
            Severity == InfoBarSeverity.Error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
    }

    private void OnActionClicked(object sender, RoutedEventArgs e)
    {
        var cmd = ActionCommand;
        if (cmd is not null && cmd.CanExecute(ActionCommandParameter))
            cmd.Execute(ActionCommandParameter);

        ActionInvoked?.Invoke(this, EventArgs.Empty);
    }

    private void OnInfoBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        IsOpen = false;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class NotificationBannerControl : NotificationBanner
{
}

public sealed partial class NotificationBannerStack : StackPanel
{
    public static readonly DependencyProperty MaxVisibleCountProperty = DependencyProperty.Register(
        nameof(MaxVisibleCount), typeof(int), typeof(NotificationBannerStack),
        new PropertyMetadata(2));

    public static readonly DependencyProperty AutoDismissSecondsProperty = DependencyProperty.Register(
        nameof(AutoDismissSeconds), typeof(double), typeof(NotificationBannerStack),
        new PropertyMetadata(6.0));

    private sealed class Entry
    {
        public required NotificationBanner Banner { get; init; }
        public DispatcherTimer? Timer { get; init; }
        public bool Paused { get; set; }
    }

    private readonly List<Entry> _entries = [];
    private readonly TextBlock _coalescedBadge;
    private int _coalescedCount;

    public NotificationBannerStack()
    {
        Spacing = 6;
        _coalescedBadge = new TextBlock
        {
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };
        Children.Add(_coalescedBadge);
        Unloaded += (_, _) => ClearAll();
    }

    public int MaxVisibleCount
    {
        get => (int)GetValue(MaxVisibleCountProperty);
        set => SetValue(MaxVisibleCountProperty, value);
    }

    public double AutoDismissSeconds
    {
        get => (double)GetValue(AutoDismissSecondsProperty);
        set => SetValue(AutoDismissSecondsProperty, value);
    }

    public void Push(NotificationRecord record, string? actionLabel = null, ICommand? actionCommand = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        int max = Math.Max(1, MaxVisibleCount);
        while (_entries.Count >= max)
        {
            int idx = _entries.FindIndex(e => e.Banner.Severity != InfoBarSeverity.Error);
            RemoveEntry(_entries[idx >= 0 ? idx : 0]);
            _coalescedCount++;
        }

        var banner = new NotificationBanner { ActionCommand = actionCommand };
        banner.Show(record, actionLabel);

        DispatcherTimer? timer = null;
        if (record.Severity != NotificationSeverity.Error && AutoDismissSeconds > 0)
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoDismissSeconds) };
        }

        var entry = new Entry { Banner = banner, Timer = timer };
        if (timer is not null)
        {
            timer.Tick += (_, _) =>
            {
                if (!entry.Paused)
                {
                    timer.Stop();
                    RemoveEntry(entry);
                }
            };
            banner.PointerEntered += (_, _) => entry.Paused = true;
            banner.PointerExited += (_, _) => entry.Paused = false;
            banner.GotFocus += (_, _) => entry.Paused = true;
            banner.LostFocus += (_, _) => entry.Paused = false;
            timer.Start();
        }

        banner.Dismissed += (_, _) => RemoveEntry(entry);

        _entries.Add(entry);
        Children.Insert(Math.Max(0, Children.Count - 1), banner);
        UpdateBadge();
    }

    public void ClearAll()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
            RemoveEntry(_entries[i]);
        _coalescedCount = 0;
        UpdateBadge();
    }

    private void RemoveEntry(Entry entry)
    {
        entry.Timer?.Stop();
        _entries.Remove(entry);
        Children.Remove(entry.Banner);
        if (_entries.Count == 0)
            _coalescedCount = 0;
        UpdateBadge();
    }

    private void UpdateBadge()
    {
        if (_coalescedCount > 0)
        {
            _coalescedBadge.Text = $"+{_coalescedCount}";
            _coalescedBadge.Visibility = Visibility.Visible;
        }
        else
        {
            _coalescedBadge.Visibility = Visibility.Collapsed;
        }
    }
}
