using Microsoft.UI.Dispatching;

namespace NativeShellKit.WinUI.Threading;

public sealed class UiDispatcherHelper
{
    private readonly DispatcherQueue _queue;

    public UiDispatcherHelper(DispatcherQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

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

    public Task EnqueueAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_queue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
        {
            tcs.SetException(new InvalidOperationException("DispatcherQueue rejected the enqueued work item."));
        }
        return tcs.Task;
    }
}
