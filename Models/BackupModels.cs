using System.Text.Json;
using TinyDb.Attributes;

namespace JxTerminal.Server.Models;

/// <summary>Metadata about a stored backup, returned by the list endpoint.</summary>
public sealed record BackupInfo(
    string Id,
    string DeviceName,
    string Comment,
    DateTime CreatedAt,
    string AppVersion,
    long SizeBytes);

/// <summary>Full on-disk representation of one backup snapshot.</summary>
[Entity("backups")]
public sealed class BackupRecord
{
    [Id]
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string AppVersion { get; set; } = "";
    public string SnapshotJson { get; set; } = "";

    public BackupInfo ToInfo() => new(
        Id,
        DeviceName,
        Comment,
        CreatedAt,
        AppVersion,
        System.Text.Encoding.UTF8.GetByteCount(SnapshotJson ?? ""));
}

/// <summary>Request body for creating a new backup.</summary>
public sealed class BackupUploadRequest
{
    public string? DeviceName { get; set; }
    public string? Comment { get; set; }
    public string? AppVersion { get; set; }

    /// <summary>The full client-exported snapshot as a JSON string.</summary>
    public string? SnapshotJson { get; set; }
}
