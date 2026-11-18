using System.Text.Json;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Abstractions.Notifications;

namespace NativeShellKit.Notifications.History;

public sealed class JsonNotificationHistoryStore : INotificationHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly int _maxStoredRecords;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonNotificationHistoryStore(AppIdentity identity, int maxStoredRecords = 500)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _filePath = identity.NotificationHistoryFilePath;
        _maxStoredRecords = Math.Max(10, maxStoredRecords);
    }

    public async Task AppendAsync(NotificationRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = await ReadAllUnsafeAsync(ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            list.RemoveAll(r => r.ExpiresUtc.HasValue && r.ExpiresUtc.Value <= now);
            list.Insert(0, record);
            if (list.Count > _maxStoredRecords)
                list.RemoveRange(_maxStoredRecords, list.Count - _maxStoredRecords);

            await WriteAllUnsafeAsync(list, ct).ConfigureAwait(false);
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
            var list = await ReadAllUnsafeAsync(ct).ConfigureAwait(false);
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
            var list = await ReadAllUnsafeAsync(ct).ConfigureAwait(false);
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
                await WriteAllUnsafeAsync(list, ct).ConfigureAwait(false);
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
            await WriteAllUnsafeAsync([], ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
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
