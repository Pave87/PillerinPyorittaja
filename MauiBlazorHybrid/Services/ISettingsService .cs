using System;
using Microsoft.Maui.Storage;

namespace MauiBlazorHybrid.Services
{
    /// <summary>
    /// Application settings persisted across app restarts.
    /// Most values use <see cref="Preferences"/>; sensitive values (password, API key)
    /// use <see cref="SecureStorage"/>.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>When true, debug-only UI and logging are enabled.</summary>
        bool IsDebugEnabled { get; set; }

        /// <summary>When true, the home screen shows doses already taken today.</summary>
        bool ShowTodayTaken { get; set; }

        /// <summary>Backup server base URL (e.g. "http://192.168.1.5:5199").</summary>
        string BackupServerUrl { get; set; }

        /// <summary>Server-side account identifier. Generated on first backup if empty.</summary>
        string AccountId { get; set; }

        /// <summary>User-chosen name for this device, used as the backup folder name on the server.</summary>
        string DeviceId { get; set; }

        /// <summary>When true, the BackupService runs a periodic auto-backup timer.</summary>
        bool AutoBackupEnabled { get; set; }

        /// <summary>Interval in minutes between auto-backup attempts.</summary>
        int AutoBackupIntervalMinutes { get; set; }

        /// <summary>
        /// Optional password for client-side AES-256-GCM encryption of backups.
        /// Stored in SecureStorage. Empty string means no encryption.
        /// </summary>
        string BackupPassword { get; set; }

        /// <summary>
        /// Optional API key for servers that require authentication.
        /// Stored in SecureStorage. Sent in the X-Api-Key header when set.
        /// </summary>
        string ApiKey { get; set; }

        /// <summary>Raised when any setting that other services react to is changed.</summary>
        event EventHandler SettingsChanged;
    }

    public class SettingsService : ISettingsService
    {
        private const string DEBUG_KEY = "debug_enabled";
        public event EventHandler? SettingsChanged;

        public bool IsDebugEnabled
        {
            get => Preferences.Default.Get(DEBUG_KEY, false);
            set
            {
                Preferences.Default.Set(DEBUG_KEY, value);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private const string ShowTodayTakenKey = "ShowTodayTaken";
        public bool ShowTodayTaken
        {
            get => Preferences.Get(ShowTodayTakenKey, false);
            set => Preferences.Set(ShowTodayTakenKey, value);
        }

        private const string BACKUP_SERVER_URL_KEY = "backup_server_url";
        public string BackupServerUrl
        {
            get => Preferences.Default.Get(BACKUP_SERVER_URL_KEY, string.Empty);
            set
            {
                Preferences.Default.Set(BACKUP_SERVER_URL_KEY, value);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private const string ACCOUNT_ID_KEY = "backup_account_id";
        public string AccountId
        {
            get => Preferences.Default.Get(ACCOUNT_ID_KEY, string.Empty);
            set => Preferences.Default.Set(ACCOUNT_ID_KEY, value);
        }

        private const string DEVICE_ID_KEY = "backup_device_id";
        public string DeviceId
        {
            get => Preferences.Default.Get(DEVICE_ID_KEY, string.Empty);
            set
            {
                Preferences.Default.Set(DEVICE_ID_KEY, value);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private const string AUTO_BACKUP_ENABLED_KEY = "auto_backup_enabled";
        public bool AutoBackupEnabled
        {
            get => Preferences.Default.Get(AUTO_BACKUP_ENABLED_KEY, false);
            set
            {
                Preferences.Default.Set(AUTO_BACKUP_ENABLED_KEY, value);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private const string AUTO_BACKUP_INTERVAL_KEY = "auto_backup_interval";
        public int AutoBackupIntervalMinutes
        {
            get => Preferences.Default.Get(AUTO_BACKUP_INTERVAL_KEY, 60);
            set
            {
                Preferences.Default.Set(AUTO_BACKUP_INTERVAL_KEY, value);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // NOTE: BackupPassword and ApiKey use SecureStorage (EncryptedSharedPreferences on Android)
        // instead of plain Preferences for security. Two known limitations:
        //
        // 1. Getter uses .GetAwaiter().GetResult() which blocks the calling thread once per app
        //    lifetime (value is cached after first access). If this ever causes a UI freeze on
        //    cold start due to slow Android keystore initialization, consider preloading these
        //    values asynchronously via an InitializeAsync() method during app startup.
        //
        // 2. Setter calls SetAsync without awaiting (fire-and-forget) because properties cannot
        //    be async. The in-memory cache is updated immediately so the app works correctly,
        //    but if the app crashes before the keystore write completes, the value reverts on
        //    next startup. This risk only exists at the moment the value is first set or changed,
        //    not during normal operation.

        /// <summary>
        /// Reads a SecureStorage value synchronously without deadlocking the UI thread.
        /// SecureStorage.GetAsync's continuation may be posted back to the calling
        /// SynchronizationContext (UI thread on Android). Blocking the UI thread while
        /// waiting for it prevents the continuation from running -> deadlock / hang on
        /// the splash screen. Task.Run hops off the UI context so the await can complete.
        /// Any SecureStorage failure is swallowed and treated as "no value stored".
        /// </summary>
        private static string ReadSecureSync(string key)
        {
            try
            {
                return Task.Run(async () => await SecureStorage.Default.GetAsync(key))
                    .GetAwaiter().GetResult() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private const string BACKUP_PASSWORD_KEY = "backup_password";
        private string? _backupPasswordCache;
        public string BackupPassword
        {
            get
            {
                _backupPasswordCache ??= ReadSecureSync(BACKUP_PASSWORD_KEY);
                return _backupPasswordCache;
            }
            set
            {
                _backupPasswordCache = value;
                if (string.IsNullOrEmpty(value))
                    SecureStorage.Default.Remove(BACKUP_PASSWORD_KEY);
                else
                    _ = SecureStorage.Default.SetAsync(BACKUP_PASSWORD_KEY, value);
            }
        }

        private const string API_KEY_KEY = "backup_api_key";
        private string? _apiKeyCache;
        public string ApiKey
        {
            get
            {
                _apiKeyCache ??= ReadSecureSync(API_KEY_KEY);
                return _apiKeyCache;
            }
            set
            {
                _apiKeyCache = value;
                if (string.IsNullOrEmpty(value))
                    SecureStorage.Default.Remove(API_KEY_KEY);
                else
                    _ = SecureStorage.Default.SetAsync(API_KEY_KEY, value);
            }
        }
    }
}
