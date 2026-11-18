using System.Security.Cryptography;
using System.Text;
using NativeShellKit.Abstractions.Identity;

namespace NativeShellKit.Security;

/// <summary>
/// Encrypts and isolates per-application secrets using Windows DPAPI (CurrentUser scope)
/// combined with application-identity-specific entropy.
/// </summary>
public sealed class DpapiSecretStore
{
    private readonly AppIdentity _identity;
    private readonly byte[] _entropy;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DpapiSecretStore(AppIdentity identity)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _entropy = Encoding.UTF8.GetBytes($"NativeShellKit.DPAPI.v1:{_identity.AppUserModelId}");
    }

    public string SecretsDirectory => _identity.SecretsDirectory;

    public async Task SaveSecretAsync(string key, string plaintextSecret, CancellationToken ct = default)
    {
        var safeKey = SafePathValidator.ValidateSafeKey(key);
        ArgumentNullException.ThrowIfNull(plaintextSecret);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_identity.SecretsDirectory);
            var target = SafePathValidator.ResolveUnderRoot(_identity.SecretsDirectory, safeKey + ".dpapi");
            var plainBytes = Encoding.UTF8.GetBytes(plaintextSecret);
            var cipherBytes = ProtectedData.Protect(plainBytes, _entropy, DataProtectionScope.CurrentUser);

            var tmp = target + ".tmp." + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(tmp, cipherBytes, ct).ConfigureAwait(false);
            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> TryLoadSecretAsync(string key, CancellationToken ct = default)
    {
        var safeKey = SafePathValidator.ValidateSafeKey(key);
        var target = SafePathValidator.ResolveUnderRoot(_identity.SecretsDirectory, safeKey + ".dpapi");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(target))
                return null;

            var cipherBytes = await File.ReadAllBytesAsync(target, ct).ConfigureAwait(false);
            var plainBytes = ProtectedData.Unprotect(cipherBytes, _entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        var safeKey = SafePathValidator.ValidateSafeKey(key);
        var target = SafePathValidator.ResolveUnderRoot(_identity.SecretsDirectory, safeKey + ".dpapi");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(target))
                return false;
            File.Delete(target);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public byte[] ProtectRaw(byte[] plaintextBytes)
    {
        ArgumentNullException.ThrowIfNull(plaintextBytes);
        return ProtectedData.Protect(plaintextBytes, _entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] UnprotectRaw(byte[] cipherBytes)
    {
        ArgumentNullException.ThrowIfNull(cipherBytes);
        return ProtectedData.Unprotect(cipherBytes, _entropy, DataProtectionScope.CurrentUser);
    }
}
