using System.Security.Cryptography;
using System.Security.Principal;
using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Security;

namespace NativeShellKit.Communication.Ipc;

/// <summary>A distinct, revocable CurrentUser grant for one versioned local integration.</summary>
public sealed class LocalPeerGrant : ILocalPeerGrant
{
    private const string SecretName = "authorized_peer";
    private readonly AppIdentity _identity;
    private readonly DpapiSecretStore _store;
    private readonly string _secretFilePath;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private IpcAuthManager? _cachedAuth;
    private DateTime _cachedMtimeUtc;
    private long _cachedLength;

    public LocalPeerGrant(AppIdentity peerIdentity)
    {
        _identity = peerIdentity ?? throw new ArgumentNullException(nameof(peerIdentity));
        _store = new(peerIdentity);
        _secretFilePath = Path.Combine(peerIdentity.SecretsDirectory, $"{SecretName}.dpapi");
        using var user = WindowsIdentity.GetCurrent();
        PipeName = $"{_identity.IpcPipeName}_{user.User!.Value}";
    }

    public string PipeName { get; }

    public async Task GrantAsync(CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cachedAuth = null;
            var key = RandomNumberGenerator.GetBytes(32);
            try { await _store.SaveSecretAsync(SecretName, Convert.ToBase64String(key), ct).ConfigureAwait(false); }
            finally { CryptographicOperations.ZeroMemory(key); }
            UpdateCachedMetadata();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task RevokeAsync(CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cachedAuth = null;
            _cachedMtimeUtc = default;
            _cachedLength = 0;
            await _store.DeleteSecretAsync(SecretName, ct).ConfigureAwait(false);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<NamedPipeIpcServer?> OpenServerAsync(CancellationToken ct = default)
    {
        var auth = await LoadAsync(ct).ConfigureAwait(false);
        return auth is null ? null : new NamedPipeIpcServer(_identity, auth, PipeName, sameUserPeer: true);
    }

    public async Task<NamedPipeIpcClient?> OpenClientAsync(CancellationToken ct = default)
    {
        var auth = await LoadAsync(ct).ConfigureAwait(false);
        return auth is null ? null : new NamedPipeIpcClient(_identity, auth, PipeName, 1500, sameUserPeer: true);
    }

    async Task<IIpcServer?> ILocalPeerGrant.OpenServerAsync(CancellationToken ct) =>
        await OpenServerAsync(ct).ConfigureAwait(false);

    async Task<IIpcClient?> ILocalPeerGrant.OpenClientAsync(CancellationToken ct) =>
        await OpenClientAsync(ct).ConfigureAwait(false);

    private async Task<IpcAuthManager?> LoadAsync(CancellationToken ct)
    {
        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_secretFilePath))
            {
                _cachedAuth = null;
                _cachedMtimeUtc = default;
                _cachedLength = 0;
                return null;
            }

            var info = new FileInfo(_secretFilePath);
            if (_cachedAuth is not null &&
                info.LastWriteTimeUtc == _cachedMtimeUtc &&
                info.Length == _cachedLength)
            {
                return _cachedAuth;
            }

            var secret = await _store.TryLoadSecretAsync(SecretName, ct).ConfigureAwait(false);
            if (secret is null)
            {
                _cachedAuth = null;
                return null;
            }

            byte[] key;
            try { key = Convert.FromBase64String(secret); }
            catch (FormatException)
            {
                _cachedAuth = null;
                return null;
            }

            try
            {
                if (key.Length != 32)
                {
                    _cachedAuth = null;
                    return null;
                }

                _cachedAuth = new IpcAuthManager(_identity, key);
                _cachedMtimeUtc = info.LastWriteTimeUtc;
                _cachedLength = info.Length;
                return _cachedAuth;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private void UpdateCachedMetadata()
    {
        if (File.Exists(_secretFilePath))
        {
            var info = new FileInfo(_secretFilePath);
            _cachedMtimeUtc = info.LastWriteTimeUtc;
            _cachedLength = info.Length;
        }
    }
}
