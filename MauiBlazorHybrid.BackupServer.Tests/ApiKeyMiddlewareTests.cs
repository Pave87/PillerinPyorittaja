using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.BackupServer.Tests;

[Collection("BackupServer")]
public class ApiKeyMiddlewareTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static List<Product> SampleProducts() => new()
    {
        new Product { Id = 1, Name = "X", Quantity = 1, LowLimit = 0, Unit = "u", AmountPerPackage = 1, Dosages = new(), History = new() }
    };

    private static ByteArrayContent JsonContent(List<Product> p)
    {
        var c = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(p));
        c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return c;
    }

    private static async Task<string> CreateAccountAsync(HttpClient client)
    {
        var resp = await client.PostAsync("/api/backup/account", null);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.GetProperty("accountId").GetString()!;
    }

    private static List<ApiKeyConfig> OneKey(string key = "secret", int maxAccounts = 5,
        long maxFileSize = 10 * 1024 * 1024, long maxStorage = 100 * 1024 * 1024,
        int rate = 60)
    {
        return new List<ApiKeyConfig>
        {
            new ApiKeyConfig
            {
                Key = key, Name = "test",
                MaxAccounts = maxAccounts,
                MaxFileSizeBytes = maxFileSize,
                MaxTotalStorageBytes = maxStorage,
                MaxRequestsPerMinute = rate,
            }
        };
    }

    #region Auth

    /// <summary>
    /// Test: Request any endpoint without X-Api-Key when keys are configured.
    /// Expectation: 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task NoApiKey_Rejects_WithUnauthorized()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both", apiKeys: OneKey());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/backup/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Test: Request with a wrong X-Api-Key value.
    /// Expectation: 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task WrongApiKey_Rejects_WithUnauthorized()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both", apiKeys: OneKey("correct"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");

        var response = await client.GetAsync("/api/backup/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Test: Request with a valid X-Api-Key.
    /// Expectation: 200 OK — middleware passes the request through.
    /// </summary>
    [Fact]
    public async Task CorrectApiKey_PassesThrough()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both", apiKeys: OneKey("correct"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "correct");

        var response = await client.GetAsync("/api/backup/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Ownership

    /// <summary>
    /// Test: Key A creates an account; key B tries to upload to that account.
    /// Expectation: 403 Forbidden — account belongs to key A.
    /// </summary>
    [Fact]
    public async Task DifferentKey_CannotAccessOthersAccount()
    {
        var keys = new List<ApiKeyConfig>
        {
            new ApiKeyConfig { Key = "A", Name = "keyA", MaxAccounts = 5, MaxFileSizeBytes = 1024*1024, MaxTotalStorageBytes = 10*1024*1024, MaxRequestsPerMinute = 60 },
            new ApiKeyConfig { Key = "B", Name = "keyB", MaxAccounts = 5, MaxFileSizeBytes = 1024*1024, MaxTotalStorageBytes = 10*1024*1024, MaxRequestsPerMinute = 60 },
        };

        using var factory = new TestServerFactory(acceptPolicy: "both", apiKeys: keys);

        using var clientA = factory.CreateClient();
        clientA.DefaultRequestHeaders.Add("X-Api-Key", "A");
        var accountId = await CreateAccountAsync(clientA);

        using var clientB = factory.CreateClient();
        clientB.DefaultRequestHeaders.Add("X-Api-Key", "B");
        var response = await clientB.PostAsync($"/api/backup/{accountId}/dev", JsonContent(SampleProducts()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Account / quota limits

    /// <summary>
    /// Test: Key with MaxAccounts = 1 creates first account OK, second is refused.
    /// Expectation: Second POST /account returns 403.
    /// </summary>
    [Fact]
    public async Task MaxAccounts_Enforced()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both", apiKeys: OneKey(maxAccounts: 1));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");

        var first = await client.PostAsync("/api/backup/account", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync("/api/backup/account", null);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    /// <summary>
    /// Test: Upload with body larger than MaxFileSizeBytes.
    /// Expectation: 413 Payload Too Large.
    /// </summary>
    [Fact]
    public async Task MaxFileSize_Enforced()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both", apiKeys: OneKey(maxFileSize: 100));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");

        var accountId = await CreateAccountAsync(client);

        // Use octet-stream to bypass JSON validation — we only care about size check.
        var big = new byte[200];
        var content = new ByteArrayContent(big);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await client.PostAsync($"/api/backup/{accountId}/dev", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    /// <summary>
    /// Test: First upload fits under quota; second upload would push total over MaxTotalStorageBytes.
    /// Expectation: First upload 200, second 413.
    /// </summary>
    [Fact]
    public async Task MaxTotalStorage_Enforced()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both",
            apiKeys: OneKey(maxFileSize: 1000, maxStorage: 300));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");

        var accountId = await CreateAccountAsync(client);

        var content1 = new ByteArrayContent(new byte[200]);
        content1.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var r1 = await client.PostAsync($"/api/backup/{accountId}/dev", content1);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var content2 = new ByteArrayContent(new byte[200]);
        content2.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var r2 = await client.PostAsync($"/api/backup/{accountId}/dev2", content2);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r2.StatusCode);
    }

    #endregion

    #region Rate limiting

    /// <summary>
    /// Test: Exceeding MaxRequestsPerMinute within a short window.
    /// Assumptions: Rate limit window is 60 seconds sliding; 3 rpm means 4th request fails.
    /// Expectation: Fourth request returns 429 Too Many Requests.
    /// </summary>
    [Fact]
    public async Task RateLimit_Enforced()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both", apiKeys: OneKey(rate: 3));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");

        var r1 = await client.GetAsync("/api/backup/health");
        var r2 = await client.GetAsync("/api/backup/health");
        var r3 = await client.GetAsync("/api/backup/health");
        var r4 = await client.GetAsync("/api/backup/health");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        Assert.Equal((HttpStatusCode)429, r4.StatusCode);
    }

    #endregion
}
