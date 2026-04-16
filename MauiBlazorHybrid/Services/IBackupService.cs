using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.Services;

/// <summary>
/// Client-side backup service. Handles communication with the backup server,
/// optional client-side AES-256-GCM encryption, restore preview/diff, and
/// optional scheduled auto-backup.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Pings the configured backup server's health endpoint.
    /// Returns true if the server responds successfully.
    /// </summary>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// Creates a new account on the server and stores the returned account id
    /// in settings. Returns the new account id, or null on failure.
    /// </summary>
    Task<string?> RegisterAccountAsync();

    /// <summary>
    /// Reads the current userdata.json, encrypts it if a password is set,
    /// and uploads it to the server under the configured account/device.
    /// Returns true on success.
    /// </summary>
    Task<bool> BackupAsync();

    /// <summary>
    /// Lists available backups for the given device, or for the current device
    /// if no deviceId is supplied.
    /// </summary>
    Task<List<BackupInfo>> ListBackupsAsync(string? deviceId = null);

    /// <summary>
    /// Lists all device ids that have backups under the current account.
    /// </summary>
    Task<List<string>> ListDevicesAsync();

    /// <summary>
    /// Downloads the latest backup and computes a diff against current local data.
    /// Returns null if download/decrypt fails.
    /// </summary>
    Task<RestorePreview?> PreviewRestoreLatestAsync(string? deviceId = null);

    /// <summary>
    /// Downloads a specific backup and computes a diff against current local data.
    /// Returns null if download/decrypt fails.
    /// </summary>
    Task<RestorePreview?> PreviewRestoreAsync(string deviceId, string backupId);

    /// <summary>
    /// Applies a previously previewed restore. Writes the data and reloads products.
    /// </summary>
    Task<bool> ApplyRestoreAsync(RestorePreview preview);

    /// <summary>
    /// Starts the periodic auto-backup timer using the configured interval.
    /// Safe to call multiple times; existing timer is replaced.
    /// </summary>
    void StartAutoBackup();

    /// <summary>
    /// Stops the periodic auto-backup timer if one is running.
    /// </summary>
    void StopAutoBackup();

    /// <summary>
    /// Called when product data has changed. If backup-on-change is enabled,
    /// triggers a backup asynchronously in the background.
    /// </summary>
    void NotifyDataChanged();

    /// <summary>
    /// Resolves the configured backup server URL and returns true if it
    /// points to a non-private (public) IP address. Returns false when
    /// the address is private, loopback, link-local, or cannot be resolved.
    /// </summary>
    Task<bool> IsNonPrivateServerAsync();
}
