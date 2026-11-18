using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Notifications;

namespace NativeShellKit.Notifications.Presenters;

/// <summary>
/// Presents native Windows App SDK toast notifications via AppNotificationManager,
/// tagging notifications per AppIdentity and forwarding activation payloads safely.
/// </summary>
public sealed class WindowsAppNotificationPresenter : INativeNotificationPresenter, IDisposable
{
    private readonly AppIdentity _identity;
    private bool _registered;
    private bool _disposed;

    public WindowsAppNotificationPresenter(AppIdentity identity, Action<string>? onActivated = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ActivatedCallback = onActivated;

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch
        {
            _registered = false;
        }
    }

    public bool IsRegistered => _registered;
    public Action<string>? ActivatedCallback { get; set; }

    public void Present(
        string sourceAppId,
        AppNotificationRequest request,
        string resolvedNotificationId,
        string launchArguments)
    {
        if (!_registered || _disposed)
            return;

        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("appId", sourceAppId)
                .AddArgument("notificationId", resolvedNotificationId)
                .AddArgument("action", "open")
                .AddText(request.Title)
                .AddText(request.Body);

            if (!string.IsNullOrWhiteSpace(request.ActivationDestination))
                builder.AddArgument("destination", request.ActivationDestination);

            if (request.Progress is not null)
            {
                builder.AddProgressBar(new AppNotificationProgressBar()
                    .SetTitle(request.Progress.Title)
                    .SetValue(Math.Clamp(request.Progress.Value, 0.0, 1.0))
                    .SetValueStringOverride(request.Progress.ValueDisplay)
                    .SetStatus(request.Progress.Status));
            }

            if (request.Actions is not null)
            {
                foreach (var action in request.Actions.Take(5))
                {
                    var btn = new AppNotificationButton(action.Label)
                        .AddArgument("appId", sourceAppId)
                        .AddArgument("notificationId", resolvedNotificationId)
                        .AddArgument("action", action.ActionId);

                    if (!string.IsNullOrWhiteSpace(action.DestinationRoute))
                        btn.AddArgument("destination", action.DestinationRoute);

                    builder.AddButton(btn);
                }
            }

            var notification = builder.BuildNotification();
            notification.Tag = TrimMax(resolvedNotificationId, 16);
            notification.Group = TrimMax(_identity.AppId, 16);
            if (request.Expiration.HasValue && request.Expiration.Value > TimeSpan.Zero)
                notification.Expiration = DateTimeOffset.UtcNow.Add(request.Expiration.Value);

            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Native notification service unavailable on headless/isolated session
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Argument))
            ActivatedCallback?.Invoke(args.Argument);
    }

    private static string TrimMax(string value, int maxLen) =>
        value.Length <= maxLen ? value : value[..maxLen];

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_registered)
        {
            _registered = false;
            try
            {
                AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
                AppNotificationManager.Default.Unregister();
            }
            catch
            {
                // Ignore cleanup failure in unpackaged or restricted sessions
            }
        }
    }
}
