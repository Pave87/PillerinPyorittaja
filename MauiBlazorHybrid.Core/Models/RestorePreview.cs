namespace MauiBlazorHybrid.Models;

/// <summary>
/// Result of comparing a backup against current local data before applying.
/// Used to determine if the restore is safe (additive only) or destructive.
/// </summary>
public class RestorePreview
{
    /// <summary>
    /// The raw JSON bytes to write if the restore is confirmed.
    /// </summary>
    public byte[] JsonBytes { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Names of products in the backup that don't exist locally (will be added).
    /// </summary>
    public List<string> AddedProducts { get; set; } = new();

    /// <summary>
    /// Names of local products that don't exist in the backup (will be lost).
    /// </summary>
    public List<string> RemovedProducts { get; set; } = new();

    /// <summary>
    /// Names of products that exist in both but have configuration differences
    /// (name, schedules, quantity, etc.). The backup version will overwrite local.
    /// </summary>
    public List<string> ModifiedProducts { get; set; } = new();

    /// <summary>
    /// Number of usage history entries in the backup that don't exist locally.
    /// </summary>
    public int AddedHistoryEntries { get; set; }

    /// <summary>
    /// Number of local usage history entries that don't exist in the backup (will be lost).
    /// </summary>
    public int LostHistoryEntries { get; set; }

    /// <summary>
    /// True if the restore would cause any data loss (removed products or lost history).
    /// </summary>
    public bool HasDestructiveChanges =>
        RemovedProducts.Count > 0 || LostHistoryEntries > 0;

    /// <summary>
    /// True if the backup is identical to current data — nothing to do.
    /// </summary>
    public bool IsIdentical =>
        AddedProducts.Count == 0 && RemovedProducts.Count == 0 &&
        ModifiedProducts.Count == 0 && AddedHistoryEntries == 0 && LostHistoryEntries == 0;
}
