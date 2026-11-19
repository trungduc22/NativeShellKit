using Microsoft.UI.Dispatching;
using NativeShellKit.Abstractions.Threading;

namespace NativeShellKit.WinUI.Threading;

public sealed class UiDispatcherHelper : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public UiDispatcherHelper(DispatcherQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public bool HasThreadAccess => _queue.HasThreadAccess;

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_queue.HasThreadAccess)
        {
            action();
            return true;
        }
        return _queue.TryEnqueue(() => action());
    }

    public Task EnqueueAsync(Action action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        if (_queue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.TryEnqueue(() =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return;
            }
            try
            {
                action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            tcs.TrySetException(new InvalidOperationException("DispatcherQueue rejected the enqueued work item."));
        }
        return tcs.Task;
    }

    public Task<T> EnqueueAsync<T>(Func<T> func, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<T>(ct);

        if (_queue.HasThreadAccess)
            return Task.FromResult(func());

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.TryEnqueue(() =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return;
            }
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            tcs.TrySetException(new InvalidOperationException("DispatcherQueue rejected the enqueued work item."));
        }
        return tcs.Task;
    }

    public Task EnqueueAsync(Func<Task> asyncAction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asyncAction);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        if (_queue.HasThreadAccess)
            return asyncAction();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.TryEnqueue(async () =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return;
            }
            try
            {
                await asyncAction().ConfigureAwait(false);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            tcs.TrySetException(new InvalidOperationException("DispatcherQueue rejected the enqueued work item."));
        }
        return tcs.Task;
    }
}

public sealed class WinUiDispatcherService : IUiDispatcher
{
    private volatile DispatcherQueue? _queue;

    public WinUiDispatcherService(DispatcherQueue? queue = null)
    {
        _queue = queue;
    }

    public void Attach(DispatcherQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    private DispatcherQueue? ResolveQueue() => _queue ?? DispatcherQueue.GetForCurrentThread();

    public bool HasThreadAccess => ResolveQueue()?.HasThreadAccess ?? false;

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var queue = ResolveQueue();
        if (queue is null)
            return false;
        return new UiDispatcherHelper(queue).TryEnqueue(action);
    }

    public Task EnqueueAsync(Action action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var queue = ResolveQueue() ?? throw new InvalidOperationException("WinUI DispatcherQueue has not been attached yet.");
        return new UiDispatcherHelper(queue).EnqueueAsync(action, ct);
    }

    public Task<T> EnqueueAsync<T>(Func<T> func, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        var queue = ResolveQueue() ?? throw new InvalidOperationException("WinUI DispatcherQueue has not been attached yet.");
        return new UiDispatcherHelper(queue).EnqueueAsync(func, ct);
    }

    public Task EnqueueAsync(Func<Task> asyncAction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asyncAction);
        var queue = ResolveQueue() ?? throw new InvalidOperationException("WinUI DispatcherQueue has not been attached yet.");
        return new UiDispatcherHelper(queue).EnqueueAsync(asyncAction, ct);
    }
}
