namespace NativeShellKit.Abstractions.Communication;

public static class IpcProtocolVersion
{
    public const string Current = "1.0";
}

public sealed record IpcCapabilitySet(
    string ProtocolVersion,
    string AppId,
    string[] SupportedOperations);

public sealed record IpcEnvelope(
    string MessageId,
    string ProtocolVersion,
    string AppId,
    string Operation,
    string PayloadJson,
    long TimestampUnixMs,
    string Nonce,
    string SignatureHex);

public delegate Task<IpcEnvelope> IpcRequestHandler(IpcEnvelope request, CancellationToken ct);

public interface IInProcessEventBus
{
    void Publish<TMessage>(TMessage message) where TMessage : notnull;
    Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default) where TMessage : notnull;
    IDisposable Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler) where TMessage : notnull;
}

public interface IIpcServer : IAsyncDisposable
{
    string PipeName { get; }
    bool IsListening { get; }
    IReadOnlyCollection<string> AllowedOperations { get; }

    void RegisterAllowedHandler(string operation, IpcRequestHandler handler);
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

public interface IIpcClient : IAsyncDisposable
{
    string PipeName { get; }
    Task<IpcCapabilitySet> HandshakeAsync(CancellationToken ct = default);
    Task<IpcEnvelope> SendAsync(string operation, string payloadJson = "{}", CancellationToken ct = default);
}

public interface ILocalPeerGrant
{
    string PipeName { get; }
    Task GrantAsync(CancellationToken ct = default);
    Task RevokeAsync(CancellationToken ct = default);
    Task<IIpcServer?> OpenServerAsync(CancellationToken ct = default);
    Task<IIpcClient?> OpenClientAsync(CancellationToken ct = default);
}
