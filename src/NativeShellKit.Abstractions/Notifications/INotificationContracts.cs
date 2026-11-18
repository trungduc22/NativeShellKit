namespace NativeShellKit.Abstractions.Notifications;

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public enum NotificationLevel
{
    All,
    WarningsAndErrorsOnly,
    Disabled
}

public sealed record NotificationAction(
    string ActionId,
    string Label,
    string? DestinationRoute = null);

public sealed record NotificationProgressData(
    string Title,
    double Value,
    string ValueDisplay,
    string Status);

public sealed record AppNotificationRequest(
    string Title,
    string Body,
    NotificationSeverity Severity = NotificationSeverity.Info,
    string Category = "General",
    string? SourceModuleId = null,
    string? NotificationId = null,
    string? DeduplicationKey = null,
    string? ActivationDestination = null,
    TimeSpan? Expiration = null,
    NotificationProgressData? Progress = null,
    IReadOnlyList<NotificationAction>? Actions = null);

public sealed class NotificationDeliveryPreferences
{
    public NotificationLevel Level { get; set; } = NotificationLevel.All;
    public bool EnableInAppBanners { get; set; } = true;
    public bool EnableNativeWindowsToasts { get; set; } = true;
    public bool QuietHoursEnabled { get; set; }
    public int QuietHoursStartHour { get; set; } = 23;
    public int QuietHoursEndHour { get; set; } = 7;
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromMinutes(10);
    public int MaxNotificationsPerMinute { get; set; } = 20;
    public HashSet<string> DisabledCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum NativeNotificationRegistrationStatus
{
    Unregistered,
    Registered,
    Failed,
    UnsupportedPlatform
}

public enum NativeNotificationSubmissionStatus
{
    NotAttempted,
    SubmittedToPlatform,
    Suppressed,
    NotRegistered,
    PlatformFailure
}

public sealed record NativeNotificationSubmissionResult(
    NativeNotificationSubmissionStatus Status,
    bool SubmittedToPlatform,
    string? PlatformNotificationTag = null,
    string? ErrorMessage = null,
    Exception? Exception = null);

public sealed record NotificationDispatchResult(
    string NotificationId,
    bool Persisted,
    bool PresentedInApp,
    bool PresentedNatively,
    string? SuppressedReason = null,
    NativeNotificationSubmissionStatus NativeSubmissionStatus = NativeNotificationSubmissionStatus.NotAttempted,
    string? NativeErrorMessage = null,
    bool VisualDisplayConfirmed = false);

public sealed record NotificationActivationArgs(
    string AppId,
    string NotificationId,
    string ActionId,
    string? DestinationRoute,
    IReadOnlyDictionary<string, string> RawParameters);

public sealed record NotificationRecord(
    string NotificationId,
    string SourceAppId,
    string? SourceModuleId,
    string Category,
    NotificationSeverity Severity,
    string Title,
    string Body,
    string? DeduplicationKey,
    string? ActivationDestination,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ExpiresUtc,
    bool IsRead);

public interface INativeNotificationPresenter
{
    bool IsRegistered { get; }
    NativeNotificationRegistrationStatus RegistrationStatus { get; }
    string? RegistrationError { get; }
    NativeNotificationSubmissionResult Present(
        string sourceAppId,
        AppNotificationRequest request,
        string resolvedNotificationId,
        string launchArguments);
}

public interface INotificationHistoryStore
{
    Task AppendAsync(NotificationRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int maxCount = 100, CancellationToken ct = default);
    Task MarkReadAsync(string notificationId, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public interface INotificationService
{
    NotificationDeliveryPreferences Preferences { get; }
    event EventHandler<NotificationRecord>? InAppNotificationRaised;
    event EventHandler<NotificationActivationArgs>? NotificationActivated;
    event EventHandler<(string RawArguments, Exception Error)>? ActivationFailed;

    Task<NotificationDispatchResult> PublishAsync(AppNotificationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationRecord>> GetHistoryAsync(int maxCount = 100, CancellationToken ct = default);
    Task ClearHistoryAsync(CancellationToken ct = default);

    string BuildActivationArguments(string notificationId, string actionId, string? destinationRoute = null);
    Task<bool> TryHandleActivationAsync(string rawArgumentString, CancellationToken ct = default);
    Task DispatchPlatformActivationAsync(string rawArgumentString, CancellationToken ct = default);
    void RegisterActionHandler(string actionId, Func<NotificationActivationArgs, Task> handler);
}
