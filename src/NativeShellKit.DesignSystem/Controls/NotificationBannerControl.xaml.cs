using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.Notifications;

namespace NativeShellKit.DesignSystem.Controls;

public sealed partial class NotificationBannerControl : UserControl
{
    public NotificationBannerControl()
    {
        InitializeComponent();
    }

    public event EventHandler? ActionInvoked;
    public event EventHandler? Dismissed;

    public void Show(NotificationRecord record, string? actionLabel = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        BannerInfoBar.Title = record.Title;
        BannerInfoBar.Message = record.Body;
        BannerInfoBar.Severity = record.Severity switch
        {
            NotificationSeverity.Success => InfoBarSeverity.Success,
            NotificationSeverity.Warning => InfoBarSeverity.Warning,
            NotificationSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };

        if (!string.IsNullOrWhiteSpace(actionLabel))
        {
            BannerActionButton.Content = actionLabel;
            BannerActionButton.Visibility = Visibility.Visible;
        }
        else
        {
            BannerActionButton.Visibility = Visibility.Collapsed;
        }

        BannerInfoBar.IsOpen = true;
    }

    private void BannerActionButton_Click(object sender, RoutedEventArgs e)
    {
        ActionInvoked?.Invoke(this, EventArgs.Empty);
    }

    private void BannerInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
