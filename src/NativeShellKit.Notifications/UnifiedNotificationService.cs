using System.Collections.Concurrent;
using System.Text;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Notifications;

namespace NativeShellKit.Notifications;

public sealed class UnifiedNotificationService : INotificationService
{
    private readonly AppIdentity _identity;
    private readonly INotificationHistoryStore _historyStore;
    private readonly INativeNotificationPresenter? _nativePresenter;
    private readonly INavigationService? _navigationService;
    private readonly IAppLifecycleService? _lifecycleService;
    private readonly Func<DateTimeOffset> _clock;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _dedupTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<NotificationActivationArgs, Task>> _actionHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTimeOffset> _recentTimestamps = new();
    private readonly object _rateGate = new();

    public UnifiedNotificationService(
        AppIdentity identity,
        INotificationHistoryStore historyStore,
        INativeNotificationPresenter? nativePresenter = null,
        INavigationService? navigationService = null,
        IAppLifecycleService? lifecycleService = null,
        Func<DateTimeOffset>? clock = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
        _nativePresenter = nativePresenter;
        _navigationService = navigationService;
        _lifecycleService = lifecycleService;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public NotificationDeliveryPreferences Preferences { get; } = new();
    public event EventHandler<NotificationRecord>? InAppNotificationRaised;
    public event EventHandler<NotificationActivationArgs>? NotificationActivated;

    public async Task<NotificationDispatchResult> PublishAsync(AppNotificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Notification title cannot be empty.", nameof(request));

        var now = _clock();
        string notificationId = string.IsNullOrWhiteSpace(request.NotificationId)
            ? Guid.NewGuid().ToString("N")[..12]
            : request.NotificationId.Trim();

        DateTimeOffset? expiresUtc = request.Expiration.HasValue && request.Expiration.Value > TimeSpan.Zero
            ? now.Add(request.Expiration.Value)
            : null;

        var record = new NotificationRecord(
            notificationId,
            _identity.AppId,
            request.SourceModuleId,
            string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim(),
            request.Severity,
            request.Title.Trim(),
            request.Body ?? string.Empty,
            request.DeduplicationKey,
            request.ActivationDestination,
            now,
            expiresUtc,
            IsRead: false);

        await _historyStore.AppendAsync(record, ct).ConfigureAwait(false);

        string? suppressedReason = EvaluateSuppressionReason(request, now);
        if (suppressedReason is not null)
        {
            return new NotificationDispatchResult(notificationId, Persisted: true, PresentedInApp: false, PresentedNatively: false, suppressedReason);
        }

        bool presentedInApp = false;
        if (Preferences.EnableInAppBanners)
        {
            InAppNotificationRaised?.Invoke(this, record);
            presentedInApp = true;
        }

        bool presentedNatively = false;
        if (Preferences.EnableNativeWindowsToasts && _nativePresenter is not null)
        {
            string launchArgs = BuildActivationArguments(notificationId, "open", request.ActivationDestination);
            _nativePresenter.Present(_identity.AppId, request, notificationId, launchArgs);
            presentedNatively = _nativePresenter.IsRegistered;
        }

        return new NotificationDispatchResult(notificationId, Persisted: true, presentedInApp, presentedNatively);
    }

    public Task<IReadOnlyList<NotificationRecord>> GetHistoryAsync(int maxCount = 100, CancellationToken ct = default) =>
        _historyStore.GetRecentAsync(maxCount, ct);

    public Task ClearHistoryAsync(CancellationToken ct = default) =>
        _historyStore.ClearAsync(ct);

    public string BuildActivationArguments(string notificationId, string actionId, string? destinationRoute = null)
    {
        if (string.IsNullOrWhiteSpace(notificationId))
            throw new ArgumentException("NotificationId cannot be empty.", nameof(notificationId));
        if (string.IsNullOrWhiteSpace(actionId))
            throw new ArgumentException("ActionId cannot be empty.", nameof(actionId));

        var sb = new StringBuilder();
        sb.Append("appId=").Append(Uri.EscapeDataString(_identity.AppId));
        sb.Append("&notificationId=").Append(Uri.EscapeDataString(notificationId.Trim()));
        sb.Append("&action=").Append(Uri.EscapeDataString(actionId.Trim()));
        if (!string.IsNullOrWhiteSpace(destinationRoute))
            sb.Append("&destination=").Append(Uri.EscapeDataString(destinationRoute.Trim()));
        return sb.ToString();
    }

    public void RegisterActionHandler(string actionId, Func<NotificationActivationArgs, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(actionId))
            throw new ArgumentException("ActionId cannot be empty.", nameof(actionId));
        ArgumentNullException.ThrowIfNull(handler);
        _actionHandlers[actionId.Trim()] = handler;
    }

    public async Task<bool> TryHandleActivationAsync(string rawArgumentString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawArgumentString))
            return false;

        var parsed = ParseArguments(rawArgumentString);
        if (!parsed.TryGetValue("appId", out var targetAppId) ||
            !string.Equals(targetAppId, _identity.AppId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!parsed.TryGetValue("notificationId", out var notificationId) || string.IsNullOrWhiteSpace(notificationId))
            return false;

        if (!parsed.TryGetValue("action", out var actionId) || string.IsNullOrWhiteSpace(actionId))
            return false;

        if (!IsSafeIdentifier(actionId))
            return false;

        parsed.TryGetValue("destination", out var destinationRoute);
        if (!string.IsNullOrWhiteSpace(destinationRoute) && !IsSafeRoute(destinationRoute))
            return false;

        var args = new NotificationActivationArgs(_identity.AppId, notificationId, actionId, destinationRoute, parsed);
        await _historyStore.MarkReadAsync(notificationId, ct).ConfigureAwait(false);

        if (_actionHandlers.TryGetValue(actionId, out var handler))
        {
            await handler(args).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(destinationRoute) && _navigationService is not null)
        {
            _navigationService.Navigate(destinationRoute);
        }

        NotificationActivated?.Invoke(this, args);

        if (_lifecycleService is not null)
        {
            await _lifecycleService.RaiseActivationAsync(
                new AppActivationEventArgs(AppActivationKind.NotificationAction, destinationRoute, rawArgumentString),
                ct).ConfigureAwait(false);
        }

        return true;
    }

    private string? EvaluateSuppressionReason(AppNotificationRequest request, DateTimeOffset now)
    {
        if (Preferences.Level == NotificationLevel.Disabled)
            return "DisabledByUser";

        if (Preferences.Level == NotificationLevel.WarningsAndErrorsOnly &&
            request.Severity is not (NotificationSeverity.Warning or NotificationSeverity.Error))
        {
            return "WarningsOnlyFilter";
        }

        if (!string.IsNullOrWhiteSpace(request.Category) && Preferences.DisabledCategories.Contains(request.Category))
            return "CategoryDisabled";

        if (IsInQuietHours(now.LocalDateTime.Hour))
            return "QuietHoursActive";

        if (!string.IsNullOrWhiteSpace(request.DeduplicationKey) && Preferences.DeduplicationWindow > TimeSpan.Zero)
        {
            if (_dedupTimestamps.TryGetValue(request.DeduplicationKey, out var lastSent) &&
                (now - lastSent) < Preferences.DeduplicationWindow)
            {
                return "Deduplicated";
            }
        }

        lock (_rateGate)
        {
            var cutoff = now.AddMinutes(-1);
            while (_recentTimestamps.Count > 0 && _recentTimestamps.Peek() < cutoff)
                _recentTimestamps.Dequeue();

            if (_recentTimestamps.Count >= Math.Max(1, Preferences.MaxNotificationsPerMinute))
                return "RateLimited";

            _recentTimestamps.Enqueue(now);
        }

        if (!string.IsNullOrWhiteSpace(request.DeduplicationKey))
            _dedupTimestamps[request.DeduplicationKey] = now;

        return null;
    }

    private bool IsInQuietHours(int hour)
    {
        if (!Preferences.QuietHoursEnabled)
            return false;

        int start = Math.Clamp(Preferences.QuietHoursStartHour, 0, 23);
        int end = Math.Clamp(Preferences.QuietHoursEndHour, 0, 23);
        return start <= end
            ? hour >= start && hour < end
            : hour >= start || hour < end;
    }

    private static Dictionary<string, string> ParseArguments(string raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in raw.Split(['&', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = segment.IndexOf('=');
            if (idx <= 0)
                continue;

            var k = Uri.UnescapeDataString(segment[..idx].Trim());
            var v = Uri.UnescapeDataString(segment[(idx + 1)..].Trim());
            if (!string.IsNullOrWhiteSpace(k))
                dict[k] = v;
        }
        return dict;
    }

    private static bool IsSafeIdentifier(string value) =>
        value.Length <= 64 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');

    private static bool IsSafeRoute(string value) =>
        value.Length <= 128 && !value.Contains("..", StringComparison.Ordinal) &&
        value.All(c => char.IsLetterOrDigit(c) || c is '/' or '_' or '-' or '.');
}
