namespace NativeShellKit.DesignSystem.Controls;

public enum ShellStatusKind
{
    Healthy,
    Active,
    Warning,
    Error,
    Offline,
    Syncing
}

[Obsolete("Use ShellStatusKind with StatusBadge instead. Preserved for 0.2.x StatusIndicatorControl compatibility.")]
public enum ShellIndicatorState
{
    Healthy,
    Active,
    Warning,
    Error,
    Offline
}

public enum ShellViewState
{
    Content,
    Loading,
    Skeleton,
    Empty,
    Error,
    Offline,
    Degraded
}

public enum MediaBarLayout
{
    Card,
    Strip
}
