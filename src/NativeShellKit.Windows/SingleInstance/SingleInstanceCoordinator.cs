using System.IO.Pipes;
using System.Text;
using NativeShellKit.Abstractions.Identity;

namespace NativeShellKit.Windows.SingleInstance;

/// <summary>
/// Per-identity single-instance session coordinator using a named Win32 Mutex and
/// asynchronous Named Pipe activation listener/signaler.
/// </summary>
public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private static readonly HashSet<string> ActiveInProcessMutexes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object InProcessLock = new();

    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool _ownsMutex;

    public SingleInstanceCoordinator(AppIdentity identity, string? mutexOverride = null, string? pipeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _mutexName = string.IsNullOrWhiteSpace(mutexOverride) ? identity.SingleInstanceMutexName : mutexOverride;
        _pipeName = string.IsNullOrWhiteSpace(pipeOverride) ? identity.ActivationPipeName : pipeOverride;
    }

    public string MutexName => _mutexName;
    public string PipeName => _pipeName;
    public bool IsPrimaryInstance => _ownsMutex;

    public event Action<string>? ActivationCommandReceived;

    public bool TryAcquirePrimaryInstance()
    {
        if (_mutex is not null)
            return _ownsMutex;

        lock (InProcessLock)
        {
            if (ActiveInProcessMutexes.Contains(_mutexName))
                return false;

            _mutex = new Mutex(initiallyOwned: true, _mutexName, out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    if (_mutex.WaitOne(TimeSpan.FromMilliseconds(50)))
                        createdNew = true;
                }
                catch (AbandonedMutexException)
                {
                    createdNew = true;
                }
            }

            _ownsMutex = createdNew;
            if (_ownsMutex)
            {
                ActiveInProcessMutexes.Add(_mutexName);
                StartActivationPipeServer();
            }
            else
            {
                _mutex.Dispose();
                _mutex = null;
            }
            return _ownsMutex;
        }
    }

    private void StartActivationPipeServer()
    {
        _listenerCts = new CancellationTokenSource();
        var ct = _listenerCts.Token;

        _listenerTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                    var msg = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        ActivationCommandReceived?.Invoke(msg.Trim());
                    }

                    await using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync("ACK").ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }, ct);
    }

    public async Task<bool> SignalExistingPrimaryInstanceAsync(
        string command = "SHOW_WINDOW",
        int timeoutMs = 1500,
        CancellationToken ct = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(command).ConfigureAwait(false);

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            var ack = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            return string.Equals(ack?.Trim(), "ACK", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_listenerCts is not null)
        {
            await _listenerCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await using var wake = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await wake.ConnectAsync(150).ConfigureAwait(false);
            }
            catch
            {
                // Ignore wake failures during disposal
            }

            if (_listenerTask is not null)
            {
                try { await Task.WhenAny(_listenerTask, Task.Delay(500)).ConfigureAwait(false); } catch { }
            }

            _listenerCts.Dispose();
            _listenerCts = null;
        }

        if (_mutex is not null)
        {
            lock (InProcessLock)
            {
                if (_ownsMutex)
                {
                    ActiveInProcessMutexes.Remove(_mutexName);
                    try { _mutex.ReleaseMutex(); } catch { }
                    _ownsMutex = false;
                }
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
