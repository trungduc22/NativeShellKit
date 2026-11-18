using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Notifications;
using NativeShellKit.Abstractions.Settings;

namespace NativeShellKit.Notifications.Presenters;

/// <summary>
/// Presents native Windows App SDK toast notifications via <see cref="AppNotificationManager"/>,
/// separating registration state, platform submission result, diagnostic errors, and supervised async activation.
/// </summary>
public sealed class WindowsAppNotificationPresenter : INativeNotificationPresenter, IDisposable
{
    private readonly AppIdentity _identity;
    private readonly ISdkLogger? _logger;
    private bool _registered;
    private bool _disposed;

    public WindowsAppNotificationPresenter(
        AppIdentity identity,
        Action<string>? onActivated = null,
        ISdkLogger? logger = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _logger = logger;
        ActivatedCallback = onActivated;

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            RegistrationStatus = NativeNotificationRegistrationStatus.UnsupportedPlatform;
            RegistrationError = "Windows 10 build 17763 or later is required for AppNotificationManager.";
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
            RegistrationStatus = NativeNotificationRegistrationStatus.Registered;
        }
        catch (Exception ex)
        {
            _registered = false;
            RegistrationStatus = NativeNotificationRegistrationStatus.Failed;
            RegistrationError = ex.Message;
            try { AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked; } catch { }
            _logger?.Log(SdkLogLevel.Warning, nameof(WindowsAppNotificationPresenter),
                $"AppNotificationManager registration failed for '{_identity.AppId}': {ex.Message}", ex);
        }
    }

    public bool IsRegistered => _registered;
    public NativeNotificationRegistrationStatus RegistrationStatus { get; private set; }
    public string? RegistrationError { get; private set; }
    public Action<string>? ActivatedCallback { get; set; }
    public Func<string, Task>? AsyncActivatedCallback { get; set; }

    public NativeNotificationSubmissionResult Present(
        string sourceAppId,
        AppNotificationRequest request,
        string resolvedNotificationId,
        string launchArguments)
    {
        if (_disposed || !_registered)
        {
            return new NativeNotificationSubmissionResult(
                NativeNotificationSubmissionStatus.NotRegistered,
                SubmittedToPlatform: false,
                ErrorMessage: _disposed
                    ? "Presenter has already been disposed."
                    : RegistrationError ?? "AppNotificationManager is not registered.");
        }

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

            _logger?.Log(SdkLogLevel.Debug, nameof(WindowsAppNotificationPresenter),
                $"Submitted native notification '{resolvedNotificationId}' (tag={notification.Tag}, group={notification.Group}).");

            return new NativeNotificationSubmissionResult(
                NativeNotificationSubmissionStatus.SubmittedToPlatform,
                SubmittedToPlatform: true,
                PlatformNotificationTag: notification.Tag);
        }
        catch (Exception ex)
        {
            _logger?.Log(SdkLogLevel.Error, nameof(WindowsAppNotificationPresenter),
                $"Native notification submission failed for '{resolvedNotificationId}': {ex.Message}", ex);

            return new NativeNotificationSubmissionResult(
                NativeNotificationSubmissionStatus.PlatformFailure,
                SubmittedToPlatform: false,
                ErrorMessage: ex.Message,
                Exception: ex);
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Argument))
            return;

        if (AsyncActivatedCallback is not null)
        {
            try
            {
                var task = AsyncActivatedCallback(args.Argument);
                task.ContinueWith(
                    faulted =>
                    {
                        var ex = faulted.Exception?.GetBaseException();
                        if (ex is not null)
                        {
                            _logger?.Log(SdkLogLevel.Error, nameof(WindowsAppNotificationPresenter),
                                $"Supervised notification activation task failed: {ex.Message}", ex);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _logger?.Log(SdkLogLevel.Error, nameof(WindowsAppNotificationPresenter),
                    $"Failed to start async notification activation handler: {ex.Message}", ex);
            }
            return;
        }

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
            RegistrationStatus = NativeNotificationRegistrationStatus.Unregistered;
            try
            {
                AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception ex)
            {
                _logger?.Log(SdkLogLevel.Debug, nameof(WindowsAppNotificationPresenter),
                    $"AppNotificationManager cleanup failed during Dispose: {ex.Message}", ex);
            }
        }
    }
}
