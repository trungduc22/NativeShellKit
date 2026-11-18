using System.Collections.Concurrent;
using NativeShellKit.Abstractions.Communication;

namespace NativeShellKit.Communication.InProcess;

public sealed class InProcessEventBus : IInProcessEventBus
{
    private readonly ConcurrentDictionary<Type, List<Func<object, CancellationToken, Task>>> _handlers = new();
    private readonly object _sync = new();

    public void Publish<TMessage>(TMessage message) where TMessage : notnull
    {
        PublishAsync(message).GetAwaiter().GetResult();
    }

    public async Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        Func<object, CancellationToken, Task>[] snapshot;
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TMessage), out var list) || list.Count == 0)
                return;
            snapshot = [.. list];
        }

        foreach (var handler in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            await handler(message, ct).ConfigureAwait(false);
        }
    }

    public IDisposable Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        Func<object, CancellationToken, Task> wrapper = (msg, ct) => handler((TMessage)msg, ct);

        lock (_sync)
        {
            var list = _handlers.GetOrAdd(typeof(TMessage), _ => []);
            list.Add(wrapper);
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                if (_handlers.TryGetValue(typeof(TMessage), out var list))
                    list.Remove(wrapper);
            }
        });
    }

    private sealed class Subscription(Action disposeAction) : IDisposable
    {
        private Action? _disposeAction = disposeAction;

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposeAction, null)?.Invoke();
        }
    }
}
