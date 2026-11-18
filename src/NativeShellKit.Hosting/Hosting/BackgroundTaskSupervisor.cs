using System.Collections.Concurrent;
using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Settings;

namespace NativeShellKit.Hosting.Hosting;

public sealed class BackgroundTaskSupervisor : IBackgroundTaskSupervisor
{
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ConcurrentDictionary<Guid, Task> _runningTasks = new();
    private readonly ISdkLogger? _logger;
    private bool _disposed;

    public BackgroundTaskSupervisor(ISdkLogger? logger = null)
    {
        _logger = logger;
    }

    public event Action<string, Exception>? TaskFailed;

    public int ActiveTaskCount => _runningTasks.Count;

    public Task RunSupervisedAsync(string taskName, Func<CancellationToken, Task> work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = Guid.NewGuid();
        var task = Task.Run(async () =>
        {
            try
            {
                await work(_shutdownCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                // Graceful shutdown
            }
            catch (Exception ex)
            {
                _logger?.Log(SdkLogLevel.Error, "BackgroundTaskSupervisor", $"Supervised task '{taskName}' failed.", ex);
                TaskFailed?.Invoke(taskName, ex);
            }
            finally
            {
                _runningTasks.TryRemove(id, out _);
            }
        }, _shutdownCts.Token);

        _runningTasks[id] = task;
        return task;
    }

    public async Task StopAllAsync(TimeSpan timeout)
    {
        if (!_shutdownCts.IsCancellationRequested)
            _shutdownCts.Cancel();

        var snapshot = _runningTasks.Values.ToArray();
        if (snapshot.Length == 0)
            return;

        var allDone = Task.WhenAll(snapshot);
        await Task.WhenAny(allDone, Task.Delay(timeout)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await StopAllAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        _shutdownCts.Dispose();
    }
}
