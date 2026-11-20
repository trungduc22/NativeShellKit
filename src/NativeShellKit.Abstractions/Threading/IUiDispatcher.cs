namespace NativeShellKit.Abstractions.Threading;

public interface IUiDispatcher
{
    bool HasThreadAccess { get; }
    bool TryEnqueue(Action action);
    Task EnqueueAsync(Action action, CancellationToken ct = default);
    Task<T> EnqueueAsync<T>(Func<T> func, CancellationToken ct = default);
    Task EnqueueAsync(Func<Task> asyncAction, CancellationToken ct = default);
}
