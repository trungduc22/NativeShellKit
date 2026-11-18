using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Security;

namespace NativeShellKit.Communication.Ipc;

internal static class IpcFraming
{
    public const int MaxPayloadBytes = 64 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task WriteEnvelopeAsync(Stream stream, IpcEnvelope envelope, CancellationToken ct)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOptions));
        if (jsonBytes.Length > MaxPayloadBytes)
            throw new InvalidOperationException($"IPC payload exceeds limit ({jsonBytes.Length} > {MaxPayloadBytes}).");

        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, jsonBytes.Length);
        await stream.WriteAsync(lenBuf, ct).ConfigureAwait(false);
        await stream.WriteAsync(jsonBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<IpcEnvelope?> ReadEnvelopeAsync(Stream stream, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        int read = await ReadExactAsync(stream, lenBuf, ct).ConfigureAwait(false);
        if (read == 0)
            return null;
        if (read < 4)
            throw new EndOfStreamException("Incomplete IPC frame header.");

        int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (len <= 0 || len > MaxPayloadBytes)
            throw new InvalidDataException($"Invalid IPC frame length {len}.");

        var payload = new byte[len];
        int payloadRead = await ReadExactAsync(stream, payload, ct).ConfigureAwait(false);
        if (payloadRead < len)
            throw new EndOfStreamException("Truncated IPC frame payload.");

        return JsonSerializer.Deserialize<IpcEnvelope>(payload, JsonOptions);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0)
                return total;
            total += n;
        }
        return total;
    }
}

public sealed class NamedPipeIpcServer : IIpcServer
{
    public const string HandshakeOperation = "__handshake";

    private readonly AppIdentity _identity;
    private readonly IpcAuthManager _auth;
    private readonly ConcurrentDictionary<string, IpcRequestHandler> _allowedHandlers = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public NamedPipeIpcServer(AppIdentity identity, IpcAuthManager auth, string? pipeNameOverride = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        PipeName = string.IsNullOrWhiteSpace(pipeNameOverride) ? identity.IpcPipeName : pipeNameOverride;
    }

    public string PipeName { get; }
    public bool IsListening { get; private set; }
    public IReadOnlyCollection<string> AllowedOperations => _allowedHandlers.Keys.ToArray();

    public void RegisterAllowedHandler(string operation, IpcRequestHandler handler)
    {
        if (string.IsNullOrWhiteSpace(operation) || string.Equals(operation, HandshakeOperation, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid IPC operation name.", nameof(operation));
        ArgumentNullException.ThrowIfNull(handler);
        _allowedHandlers[operation.Trim()] = handler;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsListening)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsListening = true;
        var token = _cts.Token;
        _acceptLoop = Task.Run(() => RunAcceptLoopAsync(token), token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsListening)
            return;

        IsListening = false;
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await using var wake = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await wake.ConnectAsync(150, ct).ConfigureAwait(false);
            }
            catch
            {
                // Best effort wake
            }
            if (_acceptLoop is not null)
            {
                try { await Task.WhenAny(_acceptLoop, Task.Delay(500, ct)).ConfigureAwait(false); } catch { }
            }
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task RunAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var req = await IpcFraming.ReadEnvelopeAsync(server, ct).ConfigureAwait(false);
                if (req is null)
                    continue;

                var resp = await HandleRequestAsync(req, ct).ConfigureAwait(false);
                await IpcFraming.WriteEnvelopeAsync(server, resp, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(30, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task<IpcEnvelope> HandleRequestAsync(IpcEnvelope req, CancellationToken ct)
    {
        if (!string.Equals(req.ProtocolVersion, IpcProtocolVersion.Current, StringComparison.Ordinal))
        {
            return _auth.CreateSignedEnvelope("ERROR", JsonSerializer.Serialize(new { error = "UNSUPPORTED_PROTOCOL", version = req.ProtocolVersion }, IpcFraming.JsonOptions));
        }

        if (!_auth.VerifyEnvelope(req))
        {
            return _auth.CreateSignedEnvelope("ERROR", JsonSerializer.Serialize(new { error = "UNAUTHORIZED" }, IpcFraming.JsonOptions));
        }

        if (string.Equals(req.Operation, HandshakeOperation, StringComparison.OrdinalIgnoreCase))
        {
            var caps = new IpcCapabilitySet(IpcProtocolVersion.Current, _identity.AppId, AllowedOperations.ToArray());
            return _auth.CreateSignedEnvelope("HANDSHAKE_OK", JsonSerializer.Serialize(caps, IpcFraming.JsonOptions));
        }

        if (!_allowedHandlers.TryGetValue(req.Operation, out var handler))
        {
            return _auth.CreateSignedEnvelope("ERROR", JsonSerializer.Serialize(new { error = "FORBIDDEN", operation = req.Operation }, IpcFraming.JsonOptions));
        }

        return await handler(req, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}

public sealed class NamedPipeIpcClient : IIpcClient
{
    private readonly IpcAuthManager _auth;
    private readonly int _connectTimeoutMs;

    public NamedPipeIpcClient(AppIdentity identity, IpcAuthManager auth, string? pipeNameOverride = null, int connectTimeoutMs = 1500)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        PipeName = string.IsNullOrWhiteSpace(pipeNameOverride) ? identity.IpcPipeName : pipeNameOverride;
        _connectTimeoutMs = Math.Max(200, connectTimeoutMs);
    }

    public string PipeName { get; }

    public async Task<IpcCapabilitySet> HandshakeAsync(CancellationToken ct = default)
    {
        var resp = await SendAsync(NamedPipeIpcServer.HandshakeOperation, "{}", ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<IpcCapabilitySet>(resp.PayloadJson, IpcFraming.JsonOptions)
            ?? throw new InvalidOperationException("Invalid IPC capability handshake response.");
    }

    public async Task<IpcEnvelope> SendAsync(string operation, string payloadJson = "{}", CancellationToken ct = default)
    {
        var envelope = _auth.CreateSignedEnvelope(operation, payloadJson);
        return await SendRawEnvelopeAsync(envelope, ct).ConfigureAwait(false);
    }

    public async Task<IpcEnvelope> SendRawEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        Exception? lastError = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_connectTimeoutMs);
                await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

                await IpcFraming.WriteEnvelopeAsync(client, envelope, timeoutCts.Token).ConfigureAwait(false);
                var resp = await IpcFraming.ReadEnvelopeAsync(client, timeoutCts.Token).ConfigureAwait(false);
                return resp ?? throw new EndOfStreamException("IPC server closed connection without response.");
            }
            catch (Exception ex) when (attempt < 2 && !ct.IsCancellationRequested)
            {
                lastError = ex;
                await Task.Delay(50 * (attempt + 1), ct).ConfigureAwait(false);
            }
        }

        throw lastError ?? new TimeoutException($"Failed to communicate with IPC pipe '{PipeName}'.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
