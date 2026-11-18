using System.Collections.Concurrent;
using System.Text.Json;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Settings;

namespace NativeShellKit.Hosting.Settings;

public sealed class JsonFileSettingsStore<T> : ISettingsStore<T> where T : class, new()
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileSettingsStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Settings file path cannot be empty.", nameof(filePath));

        FilePath = Path.GetFullPath(filePath);
        Current = LoadSynchronouslyOrDefault();
    }

    public string FilePath { get; }
    public T Current { get; private set; }

    public async Task<T> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                Current = new T();
                return Current;
            }

            var json = await File.ReadAllTextAsync(FilePath, ct).ConfigureAwait(false);
            Current = JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? new T();
            return Current;
        }
        catch
        {
            Current = new T();
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(T settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var tempFile = FilePath + ".tmp." + Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            await File.WriteAllTextAsync(tempFile, json, ct).ConfigureAwait(false);
            File.Move(tempFile, FilePath, overwrite: true);
            Current = settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    private T LoadSynchronouslyOrDefault()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new T();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? new T();
        }
        catch
        {
            return new T();
        }
    }
}

public sealed class SettingsService : ISettingsService
{
    private readonly AppIdentity _identity;
    private readonly ConcurrentDictionary<string, object> _stores = new(StringComparer.OrdinalIgnoreCase);

    public SettingsService(AppIdentity identity)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public ISettingsStore<T> GetStore<T>(string? fileName = null) where T : class, new()
    {
        var resolvedFile = string.IsNullOrWhiteSpace(fileName)
            ? _identity.SettingsFilePath
            : Path.Combine(_identity.DataDirectory, SanitizeFileName(fileName));

        return (ISettingsStore<T>)_stores.GetOrAdd(resolvedFile, static path => new JsonFileSettingsStore<T>(path));
    }

    private static string SanitizeFileName(string name)
    {
        var file = Path.GetFileName(name.Trim());
        if (string.IsNullOrWhiteSpace(file))
            throw new ArgumentException("Settings file name is invalid.", nameof(name));
        return file;
    }
}
