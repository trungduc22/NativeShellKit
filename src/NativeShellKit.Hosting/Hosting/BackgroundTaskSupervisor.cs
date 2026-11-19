using NativeShellKit.Abstractions.Lifecycle;
using NativeShellKit.Abstractions.Settings;

namespace NativeShellKit.Hosting.Hosting;

public sealed class BackgroundTaskSupervisor(ISdkLogger? logger = null) : IBackgroundTaskSupervisor
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<Guid, (string Name, Task Task)> _running = [];
    private Task? _draining;
    private bool _disposed;

    public event Action<string, Exception>? TaskFailed;

    public int ActiveTaskCount
    {
        get { lock (_gate) return _running.Count; }
    }

    public IReadOnlyCollection<string> ActiveTaskNames
    {
        get { lock (_gate) return _running.Values.Select(work => work.Name).Distinct(StringComparer.Ordinal).ToArray(); }
    }

    public Task RunSupervisedAsync(string taskName, Func<CancellationToken, Task> work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentNullException.ThrowIfNull(work);

        taskName = taskName.Trim();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_draining is not null)
                return Task.FromCanceled(_shutdown.Token);

            var id = Guid.NewGuid();
            var token = _shutdown.Token;
            var task = Task.Run(() => ExecuteAsync(id, taskName, work, token));
            _running.Add(id, (taskName, task));
            return task;
        }
    }

    public Task RunSupervisedLoopAsync(
        string taskName, TimeSpan interval, Func<CancellationToken, Task> iteration, bool runImmediately = true)
    {
        ArgumentNullException.ThrowIfNull(iteration);
        var effectiveInterval = interval > TimeSpan.Zero ? interval : TimeSpan.FromSeconds(1);
        return RunSupervisedAsync(taskName, async token =>
        {
            using var timer = new PeriodicTimer(effectiveInterval);
            if (runImmediately)
                await iteration(token).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                await iteration(token).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(Guid id, string name, Func<CancellationToken, Task> work, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            await work(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        catch (Exception ex)
        {
            logger?.Log(SdkLogLevel.Error, nameof(BackgroundTaskSupervisor), $"Supervised task '{name}' failed.", ex);
            TaskFailed?.Invoke(name, ex);
        }
        finally
        {
            lock (_gate) _running.Remove(id);
        }
    }

    public async Task StopAllAsync(TimeSpan timeout)
    {
        Task draining = BeginDrain();
        await Task.WhenAny(draining, Task.Delay(timeout)).ConfigureAwait(false);
        _ = draining.Exception;
    }

    private Task BeginDrain()
    {
        lock (_gate)
        {
            return _draining ??= Task.WhenAll(_running.Values.Select(work => work.Task).Append(_shutdown.CancelAsync()));
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        var draining = BeginDrain();
        if (await Task.WhenAny(draining, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false) == draining)
        {
            _ = draining.Exception;
            _shutdown.Dispose();
        }
        else
        {
            _ = draining.ContinueWith(task =>
            {
                _ = task.Exception;
                _shutdown.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
