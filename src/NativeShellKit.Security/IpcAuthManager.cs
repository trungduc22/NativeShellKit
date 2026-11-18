using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.Identity;

namespace NativeShellKit.Security;

public sealed class IpcAuthManager
{
    private const string SharedKeyName = "ipc_hmac_key";
    private readonly AppIdentity _identity;
    private readonly byte[] _sharedKey;
    private readonly ConcurrentDictionary<string, long> _seenNonces = new(StringComparer.Ordinal);

    public IpcAuthManager(AppIdentity identity, byte[]? explicitSharedKey = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _sharedKey = explicitSharedKey is { Length: >= 16 }
            ? [.. explicitSharedKey]
            : LoadOrCreateDpapiKey(identity);
    }

    public IpcEnvelope CreateSignedEnvelope(string operation, string payloadJson = "{}")
    {
        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("Operation cannot be empty.", nameof(operation));

        var messageId = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var unsigned = new IpcEnvelope(
            messageId,
            IpcProtocolVersion.Current,
            _identity.AppId,
            operation.Trim(),
            payloadJson ?? "{}",
            timestamp,
            nonce,
            string.Empty);

        var sig = ComputeSignatureHex(unsigned);
        return unsigned with { SignatureHex = sig };
    }

    public bool VerifyEnvelope(IpcEnvelope envelope, TimeSpan? maxClockSkew = null)
    {
        if (envelope is null)
            return false;

        if (!string.Equals(envelope.ProtocolVersion, IpcProtocolVersion.Current, StringComparison.Ordinal))
            return false;

        if (!string.Equals(envelope.AppId, _identity.AppId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(envelope.Operation) || string.IsNullOrWhiteSpace(envelope.Nonce) || string.IsNullOrWhiteSpace(envelope.SignatureHex))
            return false;

        var skew = maxClockSkew ?? TimeSpan.FromMinutes(2);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(nowMs - envelope.TimestampUnixMs) > skew.TotalMilliseconds)
            return false;

        byte[] expectedBytes;
        byte[] actualBytes;
        try
        {
            expectedBytes = Convert.FromHexString(ComputeSignatureHex(envelope));
            actualBytes = Convert.FromHexString(envelope.SignatureHex);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expectedBytes.Length != actualBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            return false;
        }

        PruneExpiredNonces(nowMs, (long)skew.TotalMilliseconds);
        return _seenNonces.TryAdd(envelope.Nonce, envelope.TimestampUnixMs);
    }

    private string ComputeSignatureHex(IpcEnvelope envelope)
    {
        var canonical = string.Join(
            '\n',
            envelope.ProtocolVersion,
            envelope.AppId,
            envelope.MessageId,
            envelope.Operation,
            envelope.TimestampUnixMs,
            envelope.Nonce,
            envelope.PayloadJson);

        using var hmac = new HMACSHA256(_sharedKey);
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(mac);
    }

    private void PruneExpiredNonces(long nowMs, long skewMs)
    {
        if (_seenNonces.Count < 1024)
            return;

        foreach (var kv in _seenNonces)
        {
            if (Math.Abs(nowMs - kv.Value) > skewMs)
                _seenNonces.TryRemove(kv.Key, out _);
        }
    }

    private static byte[] LoadOrCreateDpapiKey(AppIdentity identity)
    {
        var store = new DpapiSecretStore(identity);
        var existing = store.TryLoadSecretAsync(SharedKeyName).GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                var bytes = Convert.FromBase64String(existing);
                if (bytes.Length >= 32)
                    return bytes;
            }
            catch
            {
                // Regenerate corrupt key cleanly
            }
        }

        var generated = RandomNumberGenerator.GetBytes(32);
        store.SaveSecretAsync(SharedKeyName, Convert.ToBase64String(generated)).GetAwaiter().GetResult();
        return generated;
    }
}
