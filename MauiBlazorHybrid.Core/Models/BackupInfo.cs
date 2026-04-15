namespace MauiBlazorHybrid.Models;

/// <summary>
/// Metadata for a single backup file. Shared between server and client.
/// </summary>
public class BackupInfo
{
    /// <summary>
    /// Filename without extension (e.g. "backup_20260414_153022").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the backup was created.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Size of the backup file in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Device identifier that created the backup.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
}
