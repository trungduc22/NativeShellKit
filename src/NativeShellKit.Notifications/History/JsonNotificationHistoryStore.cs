using System.Text.Json;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Notifications;

namespace NativeShellKit.Notifications.History;

public sealed class JsonNotificationHistoryStore : INotificationHistoryStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly int _maxStoredRecords;
    private readonly bool _coalesceDiskWrites;
    private readonly TimeSpan _flushDelay;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<NotificationRecord>? _cache;
    private bool _dirty;
    private bool _flushScheduled;
    private bool _disposed;

    public JsonNotificationHistoryStore(
        AppIdentity identity,
        int maxStoredRecords = 500,
        bool coalesceDiskWrites = false,
        TimeSpan? flushDelay = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _filePath = identity.NotificationHistoryFilePath;
        _maxStoredRecords = Math.Max(10, maxStoredRecords);
        _coalesceDiskWrites = coalesceDiskWrites;
        _flushDelay = flushDelay ?? TimeSpan.FromMilliseconds(350);
    }

    public async Task AppendAsync(NotificationRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = await EnsureLoadedUnsafeAsync(ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            list.RemoveAll(r => r.ExpiresUtc.HasValue && r.ExpiresUtc.Value <= now);
            list.Insert(0, record);
            if (list.Count > _maxStoredRecords)
                list.RemoveRange(_maxStoredRecords, list.Count - _maxStoredRecords);

            await PersistOrScheduleUnsafeAsync(list, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int maxCount = 100, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var list = await EnsureLoadedUnsafeAsync(ct).ConfigureAwait(false);
            return list
                .Where(r => !r.ExpiresUtc.HasValue || r.ExpiresUtc.Value > now)
                .Take(Math.Max(1, maxCount))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkReadAsync(string notificationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(notificationId))
            return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = await EnsureLoadedUnsafeAsync(ct).ConfigureAwait(false);
            bool modified = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].NotificationId, notificationId, StringComparison.OrdinalIgnoreCase) && !list[i].IsRead)
                {
                    list[i] = list[i] with { IsRead = true };
                    modified = true;
                }
            }
            if (modified)
                await PersistOrScheduleUnsafeAsync(list, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cache = [];
            _dirty = false;
            await WriteAllUnsafeAsync(_cache, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_dirty && _cache is not null)
            {
                _dirty = false;
                await WriteAllUnsafeAsync(_cache, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { await FlushAsync().ConfigureAwait(false); } catch { }
        _gate.Dispose();
    }

    private async Task<List<NotificationRecord>> EnsureLoadedUnsafeAsync(CancellationToken ct)
    {
        if (_cache is not null)
            return _cache;

        _cache = await ReadAllUnsafeAsync(ct).ConfigureAwait(false);
        return _cache;
    }

    private async Task PersistOrScheduleUnsafeAsync(List<NotificationRecord> records, CancellationToken ct)
    {
        if (!_coalesceDiskWrites)
        {
            _dirty = false;
            await WriteAllUnsafeAsync(records, ct).ConfigureAwait(false);
            return;
        }

        _dirty = true;
        if (_flushScheduled || _disposed)
            return;

        _flushScheduled = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_flushDelay).ConfigureAwait(false);
                if (_disposed)
                    return;

                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    _flushScheduled = false;
                    if (_dirty && _cache is not null)
                    {
                        _dirty = false;
                        await WriteAllUnsafeAsync(_cache, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch
            {
                _flushScheduled = false;
            }
        });
    }

    private async Task<List<NotificationRecord>> ReadAllUnsafeAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];

            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<NotificationRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task WriteAllUnsafeAsync(List<NotificationRecord> records, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);
        var tmp = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(records, JsonOptions);
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }
}
