using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JxTerminal.Server.Models;
using TinyDb.Core;

namespace JxTerminal.Server.Services;

/// <summary>
/// Stores backup snapshots in TinyDb. Fast indexed queries by user and id,
/// without scanning files on disk.
/// </summary>
public sealed class BackupStore
{
    private readonly TinyDbEngine _db;

    public BackupStore(TinyDbEngine db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BackupInfo>> ListAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<BackupInfo>();

        var col = _db.GetCollection<BackupRecord>();
        var list = await col.FindAsync(b => b.UserId == userId);
        return list.Select(b => b.ToInfo()).OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<BackupRecord?> GetAsync(string id, string userId)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(userId)) return null;

        var col = _db.GetCollection<BackupRecord>();
        var record = await col.FindByIdAsync(id);
        return record != null && record.UserId == userId ? record : null;
    }

    public async Task<BackupRecord> CreateAsync(string userId, string? deviceName, string? comment, string snapshotJson, string? appVersion)
    {
        var record = new BackupRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            DeviceName = (deviceName ?? "").Trim(),
            Comment = (comment ?? "").Trim(),
            AppVersion = (appVersion ?? "").Trim(),
            CreatedAt = DateTime.UtcNow,
            SnapshotJson = snapshotJson ?? ""
        };

        var col = _db.GetCollection<BackupRecord>();
        await col.InsertAsync(record);
        return record;
    }

    public async Task<bool> DeleteAsync(string id, string userId)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(userId)) return false;

        var record = await GetAsync(id, userId);
        if (record == null) return false;

        var col = _db.GetCollection<BackupRecord>();
        var deleted = await col.DeleteManyAsync(b => b.Id == id && b.UserId == userId);
        return deleted > 0;
    }
}
