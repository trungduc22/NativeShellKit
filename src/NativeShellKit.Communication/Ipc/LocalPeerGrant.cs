using System.Security.Cryptography;
using System.Security.Principal;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Security;

namespace NativeShellKit.Communication.Ipc;

/// <summary>A distinct, revocable CurrentUser grant for one versioned local integration.</summary>
public sealed class LocalPeerGrant
{
    private const string SecretName = "authorized_peer";
    private readonly AppIdentity _identity;
    private readonly DpapiSecretStore _store;

    public LocalPeerGrant(AppIdentity peerIdentity)
    {
        _identity = peerIdentity;
        _store = new(peerIdentity);
        using var user = WindowsIdentity.GetCurrent();
        PipeName = $"{_identity.IpcPipeName}_{user.User!.Value}";
    }

    public string PipeName { get; }

    public async Task GrantAsync(CancellationToken ct = default)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        try { await _store.SaveSecretAsync(SecretName, Convert.ToBase64String(key), ct).ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public Task RevokeAsync(CancellationToken ct = default) => _store.DeleteSecretAsync(SecretName, ct);

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

    private async Task<IpcAuthManager?> LoadAsync(CancellationToken ct)
    {
        var secret = await _store.TryLoadSecretAsync(SecretName, ct).ConfigureAwait(false);
        if (secret is null)
            return null;
        byte[] key;
        try { key = Convert.FromBase64String(secret); }
        catch (FormatException) { return null; }
        try { return key.Length == 32 ? new(_identity, key) : null; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
