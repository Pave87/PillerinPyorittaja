namespace MauiBlazorHybrid.BackupServer;

/// <summary>
/// Server configuration loaded from appsettings.json "Backup" section.
/// </summary>
public class BackupSettings
{
    /// <summary>
    /// Filesystem path where backup data is stored. Relative paths resolve from app base directory.
    /// </summary>
    public string StoragePath { get; set; } = "./backups";

    /// <summary>
    /// Maximum number of backup files to retain per device. Oldest are pruned on upload.
    /// </summary>
    public int MaxBackupsPerDevice { get; set; } = 10;

    /// <summary>
    /// Semicolon-separated list of URLs for the HTTP listener (e.g. "http://0.0.0.0:5199").
    /// </summary>
    public string ListenUrls { get; set; } = "http://0.0.0.0:5199";

    /// <summary>
    /// Controls which backup types the server accepts on upload.
    /// "encrypted" (default) = only accept encrypted backups (application/octet-stream).
    /// "both" = accept encrypted and cleartext.
    /// "cleartext" = only accept cleartext backups (application/json).
    /// </summary>
    public string AcceptPolicy { get; set; } = "encrypted";

    /// <summary>
    /// API keys for server access. Empty list = no API key required (self-hosted mode).
    /// When populated, every request must include a valid X-Api-Key header.
    /// </summary>
    public List<ApiKeyConfig> ApiKeys { get; set; } = new();
}

/// <summary>
/// Per-key configuration for API key access. Each key has its own limits.
/// </summary>
public class ApiKeyConfig
{
    /// <summary>
    /// The API key value that clients send in the X-Api-Key header.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for this key, used in log messages.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Maximum HTTP requests allowed per minute for this key (sliding window).
    /// </summary>
    public int MaxRequestsPerMinute { get; set; } = 60;

    /// <summary>
    /// Maximum size in bytes for a single backup upload.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Maximum total storage in bytes across all backups for accounts owned by this key.
    /// </summary>
    public long MaxTotalStorageBytes { get; set; } = 100 * 1024 * 1024; // 100 MB

    /// <summary>
    /// Maximum number of accounts this key can create.
    /// </summary>
    public int MaxAccounts { get; set; } = 1;
}
