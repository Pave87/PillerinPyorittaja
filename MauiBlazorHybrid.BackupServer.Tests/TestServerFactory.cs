using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MauiBlazorHybrid.BackupServer.Tests;

/// <summary>
/// Factory that boots the real BackupServer <see cref="Program"/> with an isolated
/// temp storage directory and optional API key / accept-policy overrides.
/// Creates a fresh temp directory per instance and cleans it up on dispose.
/// </summary>
internal sealed class TestServerFactory : WebApplicationFactory<Program>
{
    public string StoragePath { get; }
    private readonly string _acceptPolicy;
    private readonly List<ApiKeyConfig> _apiKeys;

    public TestServerFactory(string acceptPolicy = "both", List<ApiKeyConfig>? apiKeys = null)
    {
        StoragePath = Path.Combine(Path.GetTempPath(), "backupserver-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StoragePath);
        _acceptPolicy = acceptPolicy;
        _apiKeys = apiKeys ?? new List<ApiKeyConfig>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("urls", "http://127.0.0.1:0");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Backup:StoragePath"] = StoragePath,
                ["Backup:MaxBackupsPerDevice"] = "10",
                ["Backup:ListenUrls"] = "http://127.0.0.1:0",
                ["Backup:AcceptPolicy"] = _acceptPolicy,
            };

            for (var i = 0; i < _apiKeys.Count; i++)
            {
                var k = _apiKeys[i];
                overrides[$"Backup:ApiKeys:{i}:Key"] = k.Key;
                overrides[$"Backup:ApiKeys:{i}:Name"] = k.Name;
                overrides[$"Backup:ApiKeys:{i}:MaxRequestsPerMinute"] = k.MaxRequestsPerMinute.ToString();
                overrides[$"Backup:ApiKeys:{i}:MaxFileSizeBytes"] = k.MaxFileSizeBytes.ToString();
                overrides[$"Backup:ApiKeys:{i}:MaxTotalStorageBytes"] = k.MaxTotalStorageBytes.ToString();
                overrides[$"Backup:ApiKeys:{i}:MaxAccounts"] = k.MaxAccounts.ToString();
            }

            config.AddInMemoryCollection(overrides);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(StoragePath))
                Directory.Delete(StoragePath, recursive: true);
        }
        catch { /* best effort */ }
    }
}

/// <summary>
/// xUnit collection used to serialize all server tests. The server's static state
/// (file lock, API key lookup, rate limit buckets) is re-initialized on every
/// factory spin-up and would race if tests ran in parallel.
/// </summary>
[CollectionDefinition("BackupServer", DisableParallelization = true)]
public class BackupServerCollection { }
