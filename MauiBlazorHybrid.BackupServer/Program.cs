using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using MauiBlazorHybrid.BackupServer;
using MauiBlazorHybrid.Models;
using Microsoft.Extensions.Options;

namespace MauiBlazorHybrid.BackupServer;

public class Program
{
    private static BackupSettings _settings = null!;
    private static string _storagePath = null!;
    private static bool _requireApiKey;
    private static Dictionary<string, ApiKeyConfig> _apiKeyLookup = null!;
    private static ConcurrentDictionary<string, RateLimitEntry> _rateLimitBuckets = null!;
    private static SemaphoreSlim _fileLock = null!;
    private static JsonSerializerOptions _jsonOptions = null!;

    public static async Task Main(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        // Skip interactive setup when stdin is redirected (tests, Docker, service host).
        if (!File.Exists(configPath) && !Console.IsInputRedirected)
        {
            await RunInteractiveSetup(configPath);
        }

        Log("Initializing server...");

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<BackupSettings>(builder.Configuration.GetSection("Backup"));

        var backupSettings = builder.Configuration.GetSection("Backup").Get<BackupSettings>() ?? new BackupSettings();
        builder.WebHost.UseUrls(backupSettings.ListenUrls.Split(';', StringSplitOptions.RemoveEmptyEntries));

        Log("Building application...");
        var app = builder.Build();

        _settings = app.Services.GetRequiredService<IOptions<BackupSettings>>().Value;
        _storagePath = Path.GetFullPath(_settings.StoragePath);
        Directory.CreateDirectory(_storagePath);
        Log($"Storage directory ready: {_storagePath}");

        _fileLock = new SemaphoreSlim(1, 1);
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        PrintStartupInfo(_settings, _storagePath);
        ConfigureApiKeyMiddleware(app);
        RegisterEndpoints(app);

        Log("Starting HTTP listener...");
        app.Run();
    }

    #region Interactive Setup

    /// <summary>
    /// Runs an interactive CLI setup to create the initial appsettings.json.
    /// </summary>
    private static async Task RunInteractiveSetup(string configPath)
    {
        Log("No configuration found. Starting interactive setup...");
        Console.WriteLine();
        Console.WriteLine("=== Backup Server Setup ===");
        Console.WriteLine();

        Console.Write("Storage path for backups [./backups]: ");
        var storagInput = Console.ReadLine()?.Trim();
        var storageSetting = string.IsNullOrEmpty(storagInput) ? "./backups" : storagInput;

        Console.Write("Max backups per device [10]: ");
        var maxInput = Console.ReadLine()?.Trim();
        var maxBackups = int.TryParse(maxInput, out var m) && m > 0 ? m : 10;

        Console.Write("Listen port [5199]: ");
        var portInput = Console.ReadLine()?.Trim();
        var port = int.TryParse(portInput, out var p) && p > 0 ? p : 5199;

        Console.Write($"Listen address [http://0.0.0.0:{port}]: ");
        var addrInput = Console.ReadLine()?.Trim();
        var listenUrls = string.IsNullOrEmpty(addrInput) ? $"http://0.0.0.0:{port}" : addrInput;

        Console.Write("Accept policy - encrypted/both/cleartext [encrypted]: ");
        var policyInput = Console.ReadLine()?.Trim()?.ToLowerInvariant();
        var acceptPolicy = policyInput is "both" or "cleartext" ? policyInput : "encrypted";

        var config = new
        {
            Backup = new
            {
                StoragePath = storageSetting,
                MaxBackupsPerDevice = maxBackups,
                ListenUrls = listenUrls,
                AcceptPolicy = acceptPolicy
            },
            Logging = new
            {
                LogLevel = new Dictionary<string, string>
                {
                    ["Default"] = "Information",
                    ["Microsoft.AspNetCore"] = "Warning"
                }
            }
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(configPath, json);
        Log($"Configuration saved to: {configPath}");
        Console.WriteLine();
    }

    #endregion

    #region API Key Middleware

    /// <summary>
    /// Configures the API key validation and rate limiting middleware.
    /// When no API keys are configured (self-hosted mode), no middleware is added.
    /// </summary>
    private static void ConfigureApiKeyMiddleware(WebApplication app)
    {
        _apiKeyLookup = _settings.ApiKeys
            .Where(k => !string.IsNullOrEmpty(k.Key))
            .ToDictionary(k => k.Key, k => k);
        _requireApiKey = _apiKeyLookup.Count > 0;
        _rateLimitBuckets = new ConcurrentDictionary<string, RateLimitEntry>();

        if (_requireApiKey)
        {
            Log($"API key protection enabled: {_apiKeyLookup.Count} key(s) configured");
            app.Use(async (context, next) =>
            {
                var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
                if (string.IsNullOrEmpty(apiKey) || !_apiKeyLookup.TryGetValue(apiKey, out var keyConfig))
                {
                    Log($"Request rejected: invalid or missing API key from {context.Connection.RemoteIpAddress}");
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
                    return;
                }

                var entry = _rateLimitBuckets.GetOrAdd(apiKey, _ => new RateLimitEntry());
                if (!entry.TryConsume(keyConfig.MaxRequestsPerMinute))
                {
                    Log($"Request rate-limited: key={keyConfig.Name}, from={context.Connection.RemoteIpAddress}");
                    context.Response.StatusCode = 429;
                    await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
                    return;
                }

                context.Items["ApiKey"] = apiKey;
                context.Items["ApiKeyConfig"] = keyConfig;
                await next();
            });
        }
        else
        {
            Log("API key protection disabled (self-hosted mode)");
        }
    }

    #endregion

    #region Account Ownership

    /// <summary>
    /// Account ownership is tracked via a _owner.txt file in each account directory.
    /// When API keys are enabled, each account belongs to the key that created it.
    /// In self-hosted mode (no API keys), ownership checks are skipped entirely.
    /// </summary>
    private static string GetOwnerFilePath(string accountPath) => Path.Combine(accountPath, "_owner.txt");

    private static void SetAccountOwner(string accountPath, string apiKey)
    {
        File.WriteAllText(GetOwnerFilePath(accountPath), apiKey);
    }

    /// <summary>
    /// Returns true if the requesting API key owns this account.
    /// Always returns true when API keys are not required (self-hosted mode).
    /// </summary>
    private static bool IsAccountOwner(string accountPath, HttpContext ctx)
    {
        if (!_requireApiKey) return true;

        var apiKey = ctx.Items["ApiKey"] as string;
        var ownerFile = GetOwnerFilePath(accountPath);
        if (!File.Exists(ownerFile)) return false;

        var owner = File.ReadAllText(ownerFile).Trim();
        return owner == apiKey;
    }

    /// <summary>
    /// Counts how many accounts are owned by the given API key.
    /// Used to enforce the per-key MaxAccounts limit.
    /// </summary>
    private static int CountAccountsForKey(string apiKey)
    {
        if (!Directory.Exists(_storagePath)) return 0;

        return Directory.GetDirectories(_storagePath)
            .Count(dir =>
            {
                var ownerFile = GetOwnerFilePath(dir);
                return File.Exists(ownerFile) && File.ReadAllText(ownerFile).Trim() == apiKey;
            });
    }

    #endregion

    #region Endpoints

    /// <summary>
    /// Registers all API endpoints on the application.
    /// </summary>
    private static void RegisterEndpoints(WebApplication app)
    {
        // Health check
        app.MapGet("/api/backup/health", (HttpContext ctx) =>
        {
            Log($"Health check from {ctx.Connection.RemoteIpAddress}");
            return Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow, acceptPolicy = _settings.AcceptPolicy });
        });

        // Create a new account
        app.MapPost("/api/backup/account", async (HttpContext ctx) =>
        {
            Log($"Account registration request from {ctx.Connection.RemoteIpAddress}");

            await _fileLock.WaitAsync();
            try
            {
                // Enforce per-key account limit
                if (_requireApiKey)
                {
                    var apiKey = ctx.Items["ApiKey"] as string;
                    var keyConfig = ctx.Items["ApiKeyConfig"] as ApiKeyConfig;
                    if (apiKey == null || keyConfig == null)
                        return Results.Json(new { error = "API key required" }, statusCode: 401);

                    var existingCount = CountAccountsForKey(apiKey);
                    if (existingCount >= keyConfig.MaxAccounts)
                    {
                        Log($"Account creation rejected: key={keyConfig.Name} already has {existingCount}/{keyConfig.MaxAccounts} account(s)");
                        return Results.Json(new { error = "Account limit reached" }, statusCode: 403);
                    }
                }

                var accountId = Guid.NewGuid().ToString("N");
                var accountPath = Path.Combine(_storagePath, accountId);
                Directory.CreateDirectory(accountPath);

                if (_requireApiKey)
                {
                    var apiKey = ctx.Items["ApiKey"] as string;
                    SetAccountOwner(accountPath, apiKey!);
                    var keyConfig = ctx.Items["ApiKeyConfig"] as ApiKeyConfig;
                    Log($"Account created: {accountId}, owner={keyConfig?.Name}");
                }
                else
                {
                    Log($"Account created: {accountId}");
                }

                return Results.Ok(new { accountId });
            }
            finally
            {
                _fileLock.Release();
            }
        });

        // Upload backup
        app.MapPost("/api/backup/{accountId}/{deviceId}", async (string accountId, string deviceId, HttpRequest request) =>
        {
            Log($"Backup upload started: account={accountId}, device={deviceId}, from={request.HttpContext.Connection.RemoteIpAddress}");

            if (!IsValidPathSegment(accountId) || !IsValidPathSegment(deviceId))
            {
                Log("Backup upload rejected: invalid account or device ID");
                return Results.BadRequest(new { error = "Invalid account or device ID" });
            }

            var accountPath = Path.Combine(_storagePath, accountId);

            await _fileLock.WaitAsync();
            try
            {
                if (!IsAccountOwner(accountPath, request.HttpContext))
                {
                    Log($"Backup upload rejected: API key does not own account={accountId}");
                    return Results.Json(new { error = "Access denied" }, statusCode: 403);
                }

                Directory.CreateDirectory(accountPath);

                // Read raw body bytes
                using var ms = new MemoryStream();
                await request.Body.CopyToAsync(ms);
                var bodyBytes = ms.ToArray();
                Log($"Backup data received: {bodyBytes.Length} bytes");

                // Enforce per-key file size and storage limits
                var keyConfig = request.HttpContext.Items["ApiKeyConfig"] as ApiKeyConfig;
                if (keyConfig != null)
                {
                    if (bodyBytes.Length > keyConfig.MaxFileSizeBytes)
                    {
                        Log($"Backup upload rejected: file size {bodyBytes.Length} exceeds limit {keyConfig.MaxFileSizeBytes} for key={keyConfig.Name}");
                        return Results.Json(new { error = "File size exceeds limit" }, statusCode: 413);
                    }

                    var currentUsage = GetAccountStorageUsage(Path.Combine(_storagePath, accountId));
                    if (currentUsage + bodyBytes.Length > keyConfig.MaxTotalStorageBytes)
                    {
                        Log($"Backup upload rejected: total storage {currentUsage + bodyBytes.Length} would exceed limit {keyConfig.MaxTotalStorageBytes} for key={keyConfig.Name}");
                        return Results.Json(new { error = "Storage quota exceeded" }, statusCode: 413);
                    }
                }

                // Determine if encrypted based on content type
                var contentType = request.ContentType ?? "";
                var isEncrypted = contentType.Contains("application/octet-stream");

                // Enforce accept policy
                if (_settings.AcceptPolicy == "encrypted" && !isEncrypted)
                {
                    Log("Backup upload rejected: server requires encrypted backups");
                    return Results.Json(new { error = "Server requires encrypted backups" }, statusCode: 400);
                }
                if (_settings.AcceptPolicy == "cleartext" && isEncrypted)
                {
                    Log("Backup upload rejected: server requires cleartext backups");
                    return Results.Json(new { error = "Server requires cleartext backups" }, statusCode: 400);
                }

                // For cleartext backups, validate the JSON structure
                if (!isEncrypted)
                {
                    try
                    {
                        var products = JsonSerializer.Deserialize<List<Product>>(bodyBytes, _jsonOptions);
                        if (products == null)
                        {
                            Log("Backup upload rejected: deserialized to null");
                            return Results.BadRequest(new { error = "Invalid data" });
                        }
                        Log($"Backup validated: {products.Count} products (cleartext)");
                    }
                    catch (JsonException ex)
                    {
                        Log($"Backup upload rejected: invalid JSON - {ex.Message}");
                        return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
                    }
                }
                else
                {
                    Log($"Backup received: encrypted ({bodyBytes.Length} bytes)");
                }

                // Write backup file
                var devicePath = Path.Combine(_storagePath, accountId, deviceId);
                var timestamp = DateTime.UtcNow;
                var extension = isEncrypted ? ".bin" : ".json";
                var fileName = $"backup_{timestamp:yyyyMMdd_HHmmss}{extension}";
                var filePath = Path.Combine(devicePath, fileName);

                Directory.CreateDirectory(devicePath);
                Log($"Writing backup to: {filePath}");
                await File.WriteAllBytesAsync(filePath, bodyBytes);

                // Prune old backups beyond the configured limit
                var pruned = PruneOldBackups(devicePath, _settings.MaxBackupsPerDevice);
                if (pruned > 0)
                {
                    Log($"Pruned {pruned} old backup(s), keeping last {_settings.MaxBackupsPerDevice}");
                }

                var fileInfo = new FileInfo(filePath);
                var backupInfo = new BackupInfo
                {
                    Id = Path.GetFileNameWithoutExtension(fileName),
                    Timestamp = timestamp,
                    SizeBytes = fileInfo.Length,
                    DeviceId = deviceId
                };

                Log($"Backup upload completed: {fileName} ({fileInfo.Length} bytes)");
                return Results.Ok(backupInfo);
            }
            finally
            {
                _fileLock.Release();
            }
        });

        // List backups for a device
        app.MapGet("/api/backup/{accountId}/{deviceId}/list", async (string accountId, string deviceId, HttpContext ctx) =>
        {
            Log($"List backups request: account={accountId}, device={deviceId}, from={ctx.Connection.RemoteIpAddress}");

            if (!IsValidPathSegment(accountId) || !IsValidPathSegment(deviceId))
            {
                Log("List backups rejected: invalid parameters");
                return Results.BadRequest(new { error = "Invalid account or device ID" });
            }

            var accountPath = Path.Combine(_storagePath, accountId);

            await _fileLock.WaitAsync();
            try
            {
                if (!IsAccountOwner(accountPath, ctx))
                {
                    Log($"List backups rejected: API key does not own account={accountId}");
                    return Results.Json(new { error = "Access denied" }, statusCode: 403);
                }

                var devicePath = Path.Combine(_storagePath, accountId, deviceId);
                if (!Directory.Exists(devicePath))
                {
                    Log($"List backups: no directory found for device={deviceId}");
                    return Results.Ok(new List<BackupInfo>());
                }

                var backups = Directory.GetFiles(devicePath, "backup_*.*")
                    .Where(f => f.EndsWith(".json") || f.EndsWith(".bin"))
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.Name)
                    .Select(f => new BackupInfo
                    {
                        Id = Path.GetFileNameWithoutExtension(f.Name),
                        Timestamp = ParseTimestampFromFileName(f.Name),
                        SizeBytes = f.Length,
                        DeviceId = deviceId
                    })
                    .ToList();

                Log($"List backups completed: {backups.Count} backup(s) found for device={deviceId}");
                return Results.Ok(backups);
            }
            finally
            {
                _fileLock.Release();
            }
        });

        // Get latest backup for a device
        app.MapGet("/api/backup/{accountId}/{deviceId}/latest", async (string accountId, string deviceId, HttpContext ctx) =>
        {
            Log($"Restore latest request: account={accountId}, device={deviceId}, from={ctx.Connection.RemoteIpAddress}");

            if (!IsValidPathSegment(accountId) || !IsValidPathSegment(deviceId))
            {
                Log("Restore latest rejected: invalid parameters");
                return Results.BadRequest(new { error = "Invalid account or device ID" });
            }

            var accountPath = Path.Combine(_storagePath, accountId);

            await _fileLock.WaitAsync();
            try
            {
                if (!IsAccountOwner(accountPath, ctx))
                {
                    Log($"Restore latest rejected: API key does not own account={accountId}");
                    return Results.Json(new { error = "Access denied" }, statusCode: 403);
                }

                var devicePath = Path.Combine(_storagePath, accountId, deviceId);
                if (!Directory.Exists(devicePath))
                {
                    Log($"Restore latest: no backups directory for device={deviceId}");
                    return Results.NotFound(new { error = "No backups found" });
                }

                var latest = Directory.GetFiles(devicePath, "backup_*.*")
                    .Where(f => f.EndsWith(".json") || f.EndsWith(".bin"))
                    .OrderByDescending(f => f)
                    .FirstOrDefault();

                if (latest == null)
                {
                    Log($"Restore latest: no backup files found for device={deviceId}");
                    return Results.NotFound(new { error = "No backups found" });
                }

                var bytes = await File.ReadAllBytesAsync(latest);
                var isEncrypted = latest.EndsWith(".bin");
                var contentType = isEncrypted ? "application/octet-stream" : "application/json";

                Log($"Restore latest completed: serving {Path.GetFileName(latest)} ({bytes.Length} bytes, {(isEncrypted ? "encrypted" : "cleartext")}) for device={deviceId}");
                return Results.Bytes(bytes, contentType);
            }
            finally
            {
                _fileLock.Release();
            }
        });

        // Get specific backup by ID
        app.MapGet("/api/backup/{accountId}/{deviceId}/{backupId}", async (string accountId, string deviceId, string backupId, HttpContext ctx) =>
        {
            Log($"Restore specific request: account={accountId}, device={deviceId}, backup={backupId}, from={ctx.Connection.RemoteIpAddress}");

            if (!IsValidPathSegment(accountId) || !IsValidPathSegment(deviceId) || !IsValidPathSegment(backupId))
            {
                Log("Restore specific rejected: invalid parameters");
                return Results.BadRequest(new { error = "Invalid parameters" });
            }

            var accountPath = Path.Combine(_storagePath, accountId);

            await _fileLock.WaitAsync();
            try
            {
                if (!IsAccountOwner(accountPath, ctx))
                {
                    Log($"Restore specific rejected: API key does not own account={accountId}");
                    return Results.Json(new { error = "Access denied" }, statusCode: 403);
                }

                // Try both extensions — backup could be encrypted (.bin) or cleartext (.json)
                var jsonPath = Path.Combine(_storagePath, accountId, deviceId, $"{backupId}.json");
                var binPath = Path.Combine(_storagePath, accountId, deviceId, $"{backupId}.bin");

                string? filePath = null;
                if (File.Exists(binPath)) filePath = binPath;
                else if (File.Exists(jsonPath)) filePath = jsonPath;

                if (filePath == null)
                {
                    Log($"Restore specific: backup not found - {backupId}");
                    return Results.NotFound(new { error = "Backup not found" });
                }

                var bytes = await File.ReadAllBytesAsync(filePath);
                var isEncrypted = filePath.EndsWith(".bin");
                var contentType = isEncrypted ? "application/octet-stream" : "application/json";

                Log($"Restore specific completed: serving {backupId} ({bytes.Length} bytes, {(isEncrypted ? "encrypted" : "cleartext")})");
                return Results.Bytes(bytes, contentType);
            }
            finally
            {
                _fileLock.Release();
            }
        });

        // List devices for an account
        app.MapGet("/api/backup/{accountId}/devices", async (string accountId, HttpContext ctx) =>
        {
            Log($"List devices request: account={accountId}, from={ctx.Connection.RemoteIpAddress}");

            if (!IsValidPathSegment(accountId))
            {
                Log("List devices rejected: invalid account ID");
                return Results.BadRequest(new { error = "Invalid account ID" });
            }

            var accountPath = Path.Combine(_storagePath, accountId);

            await _fileLock.WaitAsync();
            try
            {
                if (!IsAccountOwner(accountPath, ctx))
                {
                    Log($"List devices rejected: API key does not own account={accountId}");
                    return Results.Json(new { error = "Access denied" }, statusCode: 403);
                }

                if (!Directory.Exists(accountPath))
                {
                    Log($"List devices: no directory found for account={accountId}");
                    return Results.Ok(new List<string>());
                }

                var devices = Directory.GetDirectories(accountPath)
                    .Select(Path.GetFileName)
                    .Where(name => name != null)
                    .ToList();

                Log($"List devices completed: {devices.Count} device(s) found for account={accountId}");
                return Results.Ok(devices);
            }
            finally
            {
                _fileLock.Release();
            }
        });
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Writes a timestamped log message to the console.
    /// </summary>
    private static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
    }

    /// <summary>
    /// Validates a path segment to prevent path traversal attacks.
    /// Only allows alphanumeric characters, hyphens, and underscores.
    /// </summary>
    private static bool IsValidPathSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return false;

        if (segment.Contains("..") || segment.Contains('/') || segment.Contains('\\'))
            return false;

        return segment.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    /// <summary>
    /// Extracts a UTC timestamp from backup filenames in the format "backup_yyyyMMdd_HHmmss".
    /// </summary>
    private static DateTime ParseTimestampFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (name.StartsWith("backup_") && name.Length >= 22)
        {
            var datePart = name.Substring(7);
            if (DateTime.TryParseExact(datePart, "yyyyMMdd_HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            {
                return dt.ToUniversalTime();
            }
        }
        return DateTime.MinValue;
    }

    /// <summary>
    /// Removes oldest backup files when the count exceeds maxBackups.
    /// Returns the number of files pruned.
    /// </summary>
    private static int PruneOldBackups(string devicePath, int maxBackups)
    {
        var files = Directory.GetFiles(devicePath, "backup_*.*")
            .Where(f => f.EndsWith(".json") || f.EndsWith(".bin"))
            .OrderByDescending(f => f)
            .ToList();

        var pruned = 0;
        if (files.Count > maxBackups)
        {
            foreach (var file in files.Skip(maxBackups))
            {
                File.Delete(file);
                pruned++;
            }
        }
        return pruned;
    }

    /// <summary>
    /// Calculates total storage used by all backup files under an account directory.
    /// </summary>
    private static long GetAccountStorageUsage(string accountPath)
    {
        if (!Directory.Exists(accountPath))
            return 0;

        return Directory.GetFiles(accountPath, "backup_*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".json") || f.EndsWith(".bin"))
            .Sum(f => new FileInfo(f).Length);
    }

    /// <summary>
    /// Prints server configuration and detected local network addresses at startup.
    /// </summary>
    private static void PrintStartupInfo(BackupSettings settings, string storagePath)
    {
        Console.WriteLine();
        Log("=== Backup Server ===");
        Log($"Storage:        {storagePath}");
        Log($"Max per device: {settings.MaxBackupsPerDevice}");
        Log($"Accept policy:  {settings.AcceptPolicy}");
        Log($"Listen URLs:    {settings.ListenUrls}");
        Console.WriteLine();

        Log("Local network addresses:");
        try
        {
            var port = 5199;
            foreach (var url in settings.ListenUrls.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
                {
                    port = uri.Port;
                    break;
                }
            }

            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = iface.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        Log($"  {addr.Address}:{port}  ({iface.Name})");
                    }
                }
            }
        }
        catch
        {
            Log("  (could not detect network interfaces)");
        }

        Console.WriteLine();
        Log("Enter one of the addresses above in the app to connect.");
        Console.WriteLine();
    }

    #endregion
}

/// <summary>
/// Thread-safe sliding window rate limiter. Tracks request timestamps per key
/// and rejects requests that exceed the configured maximum per minute.
/// </summary>
class RateLimitEntry
{
    private readonly object _lock = new();
    private readonly Queue<DateTime> _timestamps = new();

    /// <summary>
    /// Attempts to consume one request from the rate limit budget.
    /// Returns false if the rate limit has been exceeded.
    /// </summary>
    public bool TryConsume(int maxPerMinute)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddMinutes(-1);

            while (_timestamps.Count > 0 && _timestamps.Peek() < windowStart)
                _timestamps.Dequeue();

            if (_timestamps.Count >= maxPerMinute)
                return false;

            _timestamps.Enqueue(now);
            return true;
        }
    }
}
