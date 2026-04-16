using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MauiBlazorHybrid.Core;
using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.Services;

/// <summary>
/// Handles backup, restore, and sync operations against the backup server.
/// Creates its own HttpClient instance to avoid sharing mutable state with other services.
/// </summary>
internal class BackupService : IBackupService
{
    #region Fields and Properties

    private readonly HttpClient _httpClient;
    private readonly IProductService _productService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _loggerService;
    private readonly BackupOnChangeHandler _backupOnChangeHandler;
    private Timer? _autoBackupTimer;
    private readonly object _timerLock = new();

    private string UserDataFilePath => Path.Combine(FileSystem.Current.AppDataDirectory, "userdata.json");
    private bool HasPassword => !string.IsNullOrEmpty(_settingsService.BackupPassword);
    private bool HasApiKey => !string.IsNullOrEmpty(_settingsService.ApiKey);
    private string AccountId => _settingsService.AccountId;

    #endregion

    #region Constructor

    public BackupService(IProductService productService, ISettingsService settingsService, ILoggerService loggerService)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _productService = productService;
        _settingsService = settingsService;
        _loggerService = loggerService;

        _backupOnChangeHandler = new BackupOnChangeHandler(
            isEnabled: () => _settingsService.BackupOnChangeEnabled,
            testConnection: TestConnectionAsync,
            runBackup: BackupAsync,
            log: msg => _loggerService.Log(msg));

        if (_settingsService.AutoBackupEnabled)
        {
            StartAutoBackup();
        }

        _productService.DataChanged += _backupOnChangeHandler.OnDataChanged;
    }

    #endregion

    #region HTTP Helpers

    /// <summary>
    /// Normalizes user-entered URL by ensuring http(s):// prefix and removing trailing slash.
    /// </summary>
    private string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }
        return url.TrimEnd('/');
    }

    /// <summary>
    /// Returns the normalized server base URL, or null if not configured.
    /// </summary>
    private string? GetBaseUrl()
    {
        var url = _settingsService.BackupServerUrl;
        if (string.IsNullOrWhiteSpace(url))
            return null;
        return NormalizeUrl(url);
    }

    /// <summary>
    /// Adds the X-Api-Key header to the request if an API key is configured.
    /// </summary>
    private void AddApiKey(HttpRequestMessage request)
    {
        if (HasApiKey)
            request.Headers.Add("X-Api-Key", _settingsService.ApiKey);
    }

    /// <summary>
    /// Sends an HTTP request with API key header attached.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        AddApiKey(request);
        return await _httpClient.SendAsync(request);
    }

    #endregion

    #region Connection and Account

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var baseUrl = GetBaseUrl();
            if (baseUrl == null) return false;

            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/backup/health");
            AddApiKey(request);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _httpClient.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Connection test failed: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> RegisterAccountAsync()
    {
        try
        {
            var baseUrl = GetBaseUrl();
            if (baseUrl == null) return null;

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/backup/account");
            var response = await SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            var accountId = result.GetProperty("accountId").GetString();

            if (!string.IsNullOrEmpty(accountId))
            {
                _settingsService.AccountId = accountId;
                _loggerService.Log($"Account registered: {accountId}");
            }

            return accountId;
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Account registration failed: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Backup

    public async Task<bool> BackupAsync()
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var accountId = AccountId;
            var deviceId = _settingsService.DeviceId;

            if (baseUrl == null || string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(deviceId))
            {
                _loggerService.Log("Backup failed: server URL, account ID, or device ID not configured");
                return false;
            }

            if (!File.Exists(UserDataFilePath))
            {
                _loggerService.Log("Backup failed: userdata.json not found");
                return false;
            }

            var fileBytes = await File.ReadAllBytesAsync(UserDataFilePath);
            HttpContent httpContent;

            if (HasPassword)
            {
                var encKey = BackupCrypto.DeriveEncryptionKey(_settingsService.BackupPassword, accountId);
                var encrypted = BackupCrypto.Encrypt(fileBytes, encKey);
                httpContent = new ByteArrayContent(encrypted);
                httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                _loggerService.Log($"Backup encrypted: {fileBytes.Length} -> {encrypted.Length} bytes");
            }
            else
            {
                httpContent = new ByteArrayContent(fileBytes);
                httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/backup/{accountId}/{deviceId}")
            {
                Content = httpContent
            };

            var response = await SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _loggerService.Log("Backup completed successfully");
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _loggerService.Log($"Backup failed: {response.StatusCode} - {error}");
            return false;
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Backup failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Listing

    public async Task<List<BackupInfo>> ListBackupsAsync(string? deviceId = null)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var accountId = AccountId;
            deviceId ??= _settingsService.DeviceId;

            if (baseUrl == null || string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(deviceId))
                return new List<BackupInfo>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/backup/{accountId}/{deviceId}/list");
            var response = await SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new List<BackupInfo>();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<BackupInfo>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new List<BackupInfo>();
        }
        catch (Exception ex)
        {
            _loggerService.Log($"List backups failed: {ex.Message}");
            return new List<BackupInfo>();
        }
    }

    public async Task<List<string>> ListDevicesAsync()
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var accountId = AccountId;

            if (baseUrl == null || string.IsNullOrWhiteSpace(accountId))
                return new List<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/backup/{accountId}/devices");
            var response = await SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new List<string>();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (Exception ex)
        {
            _loggerService.Log($"List devices failed: {ex.Message}");
            return new List<string>();
        }
    }

    #endregion

    #region Restore

    public async Task<RestorePreview?> PreviewRestoreLatestAsync(string? deviceId = null)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var accountId = AccountId;
            deviceId ??= _settingsService.DeviceId;

            if (baseUrl == null || string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(deviceId))
                return null;

            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/backup/{accountId}/{deviceId}/latest");
            var response = await SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _loggerService.Log($"Restore preview failed: {response.StatusCode}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            return await BuildRestorePreviewAsync(bytes, contentType);
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Restore preview failed: {ex.Message}");
            return null;
        }
    }

    public async Task<RestorePreview?> PreviewRestoreAsync(string deviceId, string backupId)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var accountId = AccountId;

            if (baseUrl == null || string.IsNullOrWhiteSpace(accountId))
                return null;

            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/backup/{accountId}/{deviceId}/{backupId}");
            var response = await SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _loggerService.Log($"Restore preview failed: {response.StatusCode}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            return await BuildRestorePreviewAsync(bytes, contentType);
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Restore preview failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Applies a previously previewed restore by writing the JSON to disk and reloading products.
    /// </summary>
    public async Task<bool> ApplyRestoreAsync(RestorePreview preview)
    {
        try
        {
            await File.WriteAllBytesAsync(UserDataFilePath, preview.JsonBytes);
            await _productService.ReloadAsync();
            _loggerService.Log("Restore applied successfully");
            return true;
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Restore apply failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Decrypts (if needed), validates, and compares the backup data against current local products.
    /// </summary>
    private async Task<RestorePreview?> BuildRestorePreviewAsync(byte[] data, string contentType)
    {
        // Decrypt if encrypted
        byte[] jsonBytes;
        if (contentType.Contains("octet-stream"))
        {
            if (!HasPassword)
            {
                _loggerService.Log("Restore preview failed: backup is encrypted but no password is set");
                return null;
            }

            try
            {
                var encKey = BackupCrypto.DeriveEncryptionKey(_settingsService.BackupPassword, AccountId);
                jsonBytes = BackupCrypto.Decrypt(data, encKey);
                _loggerService.Log($"Backup decrypted: {data.Length} -> {jsonBytes.Length} bytes");
            }
            catch (Exception ex)
            {
                _loggerService.Log($"Restore preview failed: decryption error - {ex.Message}");
                return null;
            }
        }
        else
        {
            jsonBytes = data;
        }

        // Parse backup products
        List<Product>? backupProducts;
        try
        {
            backupProducts = JsonSerializer.Deserialize<List<Product>>(jsonBytes);
            if (backupProducts == null)
            {
                _loggerService.Log("Restore preview failed: invalid data");
                return null;
            }
        }
        catch (JsonException ex)
        {
            _loggerService.Log($"Restore preview failed: invalid JSON - {ex.Message}");
            return null;
        }

        // Load current local products and delegate diff computation to the pure helper in Core.
        var localProducts = await _productService.GetProductsAsync();
        var preview = RestorePreviewBuilder.Build(localProducts, backupProducts, jsonBytes);

        _loggerService.Log($"Restore preview: +{preview.AddedProducts.Count} products, " +
            $"-{preview.RemovedProducts.Count} products, ~{preview.ModifiedProducts.Count} modified, " +
            $"+{preview.AddedHistoryEntries} history, -{preview.LostHistoryEntries} history");

        return preview;
    }

    #endregion

    #region Auto-Backup

    public void StartAutoBackup()
    {
        lock (_timerLock)
        {
            StopAutoBackupInternal();

            var intervalMs = _settingsService.AutoBackupIntervalMinutes * 60 * 1000;
            _autoBackupTimer = new Timer(async _ =>
            {
                try
                {
                    if (!_settingsService.AutoBackupEnabled) return;

                    var connected = await TestConnectionAsync();
                    if (connected)
                    {
                        await BackupAsync();
                    }
                }
                catch (Exception ex)
                {
                    _loggerService.Log($"Auto-backup failed: {ex.Message}");
                }
            }, null, intervalMs, intervalMs);

            _loggerService.Log($"Auto-backup started with interval {_settingsService.AutoBackupIntervalMinutes} minutes");
        }
    }

    public void StopAutoBackup()
    {
        lock (_timerLock)
        {
            StopAutoBackupInternal();
        }
    }

    private void StopAutoBackupInternal()
    {
        _autoBackupTimer?.Dispose();
        _autoBackupTimer = null;
        _loggerService.Log("Auto-backup stopped");
    }

    public void NotifyDataChanged()
    {
        _backupOnChangeHandler.OnDataChanged();
    }

    #endregion

    #region Server Address Validation

    public async Task<bool> IsNonPrivateServerAsync()
    {
        try
        {
            var baseUrl = GetBaseUrl();
            if (string.IsNullOrWhiteSpace(baseUrl))
                return false;

            var uri = new Uri(baseUrl);
            var host = uri.Host;

            IPAddress[] addresses;
            if (IPAddress.TryParse(host, out var directIp))
            {
                addresses = [directIp];
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(host);
            }

            if (addresses.Length == 0)
                return false;

            foreach (var address in addresses)
            {
                if (!NetworkAddressHelper.IsPrivateAddress(address))
                    return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Server address check failed: {ex.Message}");
            return false;
        }
    }

    #endregion
}
