using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.Security;

namespace NativeShellKit.Diagnostics.Logging;

public sealed class RedactingFileLogger : ISdkLogger
{
    private readonly AppIdentity _identity;
    private readonly object _sync = new();
    private readonly Queue<string> _recentLines = new();
    private readonly int _maxBufferedLines;

    public RedactingFileLogger(AppIdentity identity, int maxBufferedLines = 500)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _maxBufferedLines = Math.Max(20, maxBufferedLines);
    }

    public string CurrentLogFilePath => Path.Combine(
        _identity.LogsDirectory,
        $"sdk-{DateTimeOffset.UtcNow:yyyyMMdd}.log");

    public void Log(SdkLogLevel level, string category, string message, Exception? exception = null)
    {
        var safeCategory = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
        var safeMsg = SecretRedactor.Redact(message);
        var line = $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{safeCategory}] {safeMsg}";
        if (exception is not null)
        {
            line += $" | {SecretRedactor.Redact(exception.ToString())}";
        }

        lock (_sync)
        {
            if (_recentLines.Count >= _maxBufferedLines)
                _recentLines.Dequeue();
            _recentLines.Enqueue(line);

            try
            {
                Directory.CreateDirectory(_identity.LogsDirectory);
                File.AppendAllText(CurrentLogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Avoid throwing from diagnostic logger
            }
        }
    }

    public IReadOnlyList<string> GetRecentLines(int count = 100)
    {
        lock (_sync)
        {
            return _recentLines.TakeLast(Math.Max(1, count)).ToArray();
        }
    }
}
