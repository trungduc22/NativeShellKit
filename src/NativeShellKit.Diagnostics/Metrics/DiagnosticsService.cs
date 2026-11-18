using System.Diagnostics;
using NativeShellKit.Abstractions.Settings;

namespace NativeShellKit.Diagnostics.Metrics;

/// <summary>
/// Collects honest startup timing and process memory/CPU metrics without manipulating working-set size.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<StartupTimingEntry> _phases = [];
    private readonly object _sync = new();

    public void RecordStartupPhase(string phaseName, double durationMs)
    {
        if (string.IsNullOrWhiteSpace(phaseName))
            return;

        lock (_sync)
        {
            _phases.Add(new StartupTimingEntry(phaseName.Trim(), Math.Round(Math.Max(0, durationMs), 2)));
        }
    }

    public StartupTimingReport GetStartupTimings()
    {
        lock (_sync)
        {
            return new StartupTimingReport(
                _startedUtc,
                Math.Round(_clock.Elapsed.TotalMilliseconds, 2),
                [.. _phases]);
        }
    }

    public RuntimeMetricsSnapshot CaptureMetrics()
    {
        using var proc = Process.GetCurrentProcess();
        proc.Refresh();

        return new RuntimeMetricsSnapshot(
            DateTimeOffset.UtcNow,
            proc.WorkingSet64,
            proc.PrivateMemorySize64,
            GC.GetTotalMemory(false),
            Math.Round(proc.TotalProcessorTime.TotalSeconds, 3),
            proc.Threads.Count);
    }
}
