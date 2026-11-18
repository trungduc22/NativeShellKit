namespace NativeShellKit.Abstractions.Settings;

public interface ISettingsStore<T> where T : class, new()
{
    string FilePath { get; }
    T Current { get; }
    Task<T> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(T settings, CancellationToken ct = default);
}

public interface ISettingsService
{
    ISettingsStore<T> GetStore<T>(string? fileName = null) where T : class, new();
}

public enum SdkLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface ISdkLogger
{
    void Log(SdkLogLevel level, string category, string message, Exception? exception = null);
    IReadOnlyList<string> GetRecentLines(int count = 100);
}

public sealed record StartupTimingEntry(string PhaseName, double DurationMs);

public sealed record StartupTimingReport(
    DateTimeOffset StartedUtc,
    double TotalStartupMs,
    IReadOnlyList<StartupTimingEntry> Phases);

public sealed record RuntimeMetricsSnapshot(
    DateTimeOffset CapturedUtc,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long GcTotalMemoryBytes,
    double TotalProcessorSeconds,
    int ThreadCount);

public sealed record UiSelfTestStepResult(string StepName, bool Passed, string Detail);

public sealed record UiSelfTestReport(
    bool AllPassed,
    DateTimeOffset ExecutedUtc,
    IReadOnlyList<UiSelfTestStepResult> Steps);

public interface IDiagnosticsService
{
    StartupTimingReport GetStartupTimings();
    RuntimeMetricsSnapshot CaptureMetrics();
    void RecordStartupPhase(string phaseName, double durationMs);
}
