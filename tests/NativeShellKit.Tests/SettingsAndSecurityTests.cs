using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Hosting.Settings;
using NativeShellKit.Security;
using Xunit;

namespace NativeShellKit.Tests;

public sealed class SettingsAndSecurityTests
{
    public sealed class SampleSettings
    {
        public string Mode { get; set; } = "Default";
        public int Limit { get; set; } = 10;
    }

    [Fact]
    public async Task SettingsStore_PersistsAtomically_AndRecoversFromCorruptJson()
    {
        using var tmp = new TempRootScope();
        var identity = new AppIdentity("settingsapp", "Settings App", baseDataRoot: tmp.RootPath);
        var store = new JsonFileSettingsStore<SampleSettings>(identity.SettingsFilePath);

        await store.SaveAsync(new SampleSettings { Mode = "Dark", Limit = 42 });
        var reloaded = await store.LoadAsync();
        Assert.Equal("Dark", reloaded.Mode);
        Assert.Equal(42, reloaded.Limit);

        await File.WriteAllTextAsync(identity.SettingsFilePath, "{corrupt json");
        var recovered = await store.LoadAsync();
        Assert.Equal("Default", recovered.Mode);
        Assert.Equal(10, recovered.Limit);
    }

    [Fact]
    public async Task DpapiSecretStore_IsolatesSecretsAcrossIdentities()
    {
        using var tmp = new TempRootScope();
        var idA = new AppIdentity("app-alpha", "App Alpha", baseDataRoot: tmp.RootPath);
        var idB = new AppIdentity("app-beta", "App Beta", baseDataRoot: tmp.RootPath);

        var storeA = new DpapiSecretStore(idA);
        var storeB = new DpapiSecretStore(idB);

        await storeA.SaveSecretAsync("api-key", "super-secret-value-123");
        Assert.Equal("super-secret-value-123", await storeA.TryLoadSecretAsync("api-key"));

        // Copy raw encrypted .dpapi file into idB's secret folder: idB's entropy rejects decryption cleanly
        Directory.CreateDirectory(idB.SecretsDirectory);
        File.Copy(
            Path.Combine(idA.SecretsDirectory, "api-key.dpapi"),
            Path.Combine(idB.SecretsDirectory, "api-key.dpapi"),
            overwrite: true);

        Assert.Null(await storeB.TryLoadSecretAsync("api-key"));
        Assert.True(await storeA.DeleteSecretAsync("api-key"));
        Assert.Null(await storeA.TryLoadSecretAsync("api-key"));
    }

    [Fact]
    public void SafePathValidator_BlocksTraversalRootedStreamsAndReservedNames()
    {
        using var tmp = new TempRootScope();

        Assert.Throws<ArgumentException>(() => SafePathValidator.ValidateSafeKey("../evil"));
        Assert.Throws<ArgumentException>(() => SafePathValidator.ValidateSafeKey("CON"));
        Assert.Throws<ArgumentException>(() => SafePathValidator.ValidateSafeKey("lpt1"));

        Assert.Throws<ArgumentException>(() => SafePathValidator.ResolveUnderRoot(tmp.RootPath, @"C:\Windows\System32\cmd.exe"));
        Assert.Throws<ArgumentException>(() => SafePathValidator.ResolveUnderRoot(tmp.RootPath, "file.txt:stream"));
        Assert.Throws<InvalidOperationException>(() => SafePathValidator.ResolveUnderRoot(tmp.RootPath, @"..\escape.txt"));
        Assert.Throws<InvalidOperationException>(() => SafePathValidator.ResolveUnderRoot(tmp.RootPath, @"sub\NUL.json"));

        var safe = SafePathValidator.ResolveUnderRoot(tmp.RootPath, @"nested\config.json");
        Assert.StartsWith(Path.GetFullPath(tmp.RootPath), safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecretRedactor_RedactsBearerKeyValuesAndKnownPrefixes()
    {
        string raw = "Authorization: Bearer abc123.def456== api_key=my_secret_99 password: 'pw123' ghp_0123456789abcdef0123 AIzaSyD_ABCDEFGHIJKLMNOPQRSTUVWXYZ12 sk-proj-1234567890abcdef";
        string redacted = SecretRedactor.Redact(raw);

        Assert.DoesNotContain("abc123.def456", redacted);
        Assert.DoesNotContain("my_secret_99", redacted);
        Assert.DoesNotContain("pw123", redacted);
        Assert.DoesNotContain("ghp_0123456789abcdef0123", redacted);
        Assert.DoesNotContain("AIzaSyD_ABCDEFGHIJKLMNOPQRSTUVWXYZ12", redacted);
        Assert.DoesNotContain("sk-proj-1234567890abcdef", redacted);
        Assert.Contains(SecretRedactor.RedactedPlaceholder, redacted);
    }
}
