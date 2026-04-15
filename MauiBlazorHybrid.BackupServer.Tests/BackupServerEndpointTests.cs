using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MauiBlazorHybrid.Core;
using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.BackupServer.Tests;

[Collection("BackupServer")]
public class BackupServerEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static List<Product> SampleProducts() => new()
    {
        new Product
        {
            Id = 1, Name = "TestPill", Quantity = 30, LowLimit = 5,
            Unit = "pill", AmountPerPackage = 30,
            Dosages = new(), History = new()
        }
    };

    private static ByteArrayContent JsonContent(List<Product> products)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(products);
        var c = new ByteArrayContent(bytes);
        c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return c;
    }

    private static ByteArrayContent OctetContent(byte[] bytes)
    {
        var c = new ByteArrayContent(bytes);
        c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return c;
    }

    private static async Task<string> CreateAccountAsync(HttpClient client)
    {
        var resp = await client.PostAsync("/api/backup/account", null);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.GetProperty("accountId").GetString()!;
    }

    #region Health

    /// <summary>
    /// Test: GET /api/backup/health returns 200 and a status payload.
    /// Expectation: Server responds with JSON containing status=ok.
    /// </summary>
    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/backup/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }

    #endregion

    #region Account creation

    /// <summary>
    /// Test: POST /api/backup/account creates a new account and returns its id.
    /// Expectation: 200 with a non-empty accountId and a matching directory on disk.
    /// </summary>
    [Fact]
    public async Task CreateAccount_WithoutApiKey_ReturnsAccountId()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);

        Assert.False(string.IsNullOrEmpty(accountId));
        Assert.True(Directory.Exists(Path.Combine(factory.StoragePath, accountId)));
    }

    #endregion

    #region Upload

    /// <summary>
    /// Test: Upload a valid cleartext JSON backup to a device.
    /// Expectation: 200 with BackupInfo, and the file lands on disk under accountId/deviceId.
    /// </summary>
    [Fact]
    public async Task Upload_CleartextJson_WritesFile()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        var response = await client.PostAsync($"/api/backup/{accountId}/device1", JsonContent(SampleProducts()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<BackupInfo>(JsonOpts);
        Assert.NotNull(info);
        Assert.False(string.IsNullOrEmpty(info!.Id));

        var devicePath = Path.Combine(factory.StoragePath, accountId, "device1");
        Assert.Single(Directory.GetFiles(devicePath, "backup_*.json"));
    }

    /// <summary>
    /// Test: Upload cleartext that cannot deserialize as List&lt;Product&gt;.
    /// Expectation: 400 Bad Request; nothing is written.
    /// </summary>
    [Fact]
    public async Task Upload_InvalidJson_ReturnsBadRequest()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        var bad = new ByteArrayContent(Encoding.UTF8.GetBytes("{ not json"));
        bad.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync($"/api/backup/{accountId}/device1", bad);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var devicePath = Path.Combine(factory.StoragePath, accountId, "device1");
        Assert.False(Directory.Exists(devicePath) && Directory.GetFiles(devicePath).Length > 0);
    }

    /// <summary>
    /// Test: Upload encrypted backup when AcceptPolicy = "cleartext".
    /// Expectation: 400 Bad Request — server refuses encrypted data.
    /// </summary>
    [Fact]
    public async Task Upload_EncryptedRejected_WhenPolicyIsCleartext()
    {
        using var factory = new TestServerFactory(acceptPolicy: "cleartext");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        var response = await client.PostAsync($"/api/backup/{accountId}/device1", OctetContent(new byte[] { 1, 2, 3 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Test: Upload cleartext when AcceptPolicy = "encrypted".
    /// Expectation: 400 Bad Request — server refuses plaintext.
    /// </summary>
    [Fact]
    public async Task Upload_CleartextRejected_WhenPolicyIsEncrypted()
    {
        using var factory = new TestServerFactory(acceptPolicy: "encrypted");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        var response = await client.PostAsync($"/api/backup/{accountId}/device1", JsonContent(SampleProducts()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Test: Upload encrypted backup when AcceptPolicy = "both".
    /// Expectation: 200 OK and a .bin file is written.
    /// </summary>
    [Fact]
    public async Task Upload_EncryptedAccepted_WhenPolicyIsBoth()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        var response = await client.PostAsync($"/api/backup/{accountId}/device1", OctetContent(new byte[] { 9, 9, 9 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var devicePath = Path.Combine(factory.StoragePath, accountId, "device1");
        Assert.Single(Directory.GetFiles(devicePath, "backup_*.bin"));
    }

    /// <summary>
    /// Test: Upload with a path-traversal attempt in deviceId.
    /// Expectation: 400 Bad Request — validator rejects path segments containing ".." or slashes.
    /// </summary>
    [Fact]
    public async Task Upload_InvalidDeviceId_ReturnsBadRequest()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        var response = await client.PostAsync($"/api/backup/{accountId}/..%2Fevil", JsonContent(SampleProducts()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region List / Restore

    /// <summary>
    /// Test: List backups for a device after uploading two cleartext backups.
    /// Expectation: Returns 2 BackupInfo entries, newest first.
    /// </summary>
    [Fact]
    public async Task ListBackups_ReturnsUploadedBackups()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);

        // Two uploads with distinct filenames (second waits 1s so timestamp differs).
        await client.PostAsync($"/api/backup/{accountId}/deviceA", JsonContent(SampleProducts()));
        await Task.Delay(1100);
        await client.PostAsync($"/api/backup/{accountId}/deviceA", JsonContent(SampleProducts()));

        var listResp = await client.GetAsync($"/api/backup/{accountId}/deviceA/list");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<List<BackupInfo>>(JsonOpts);
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        // Sorted newest-first
        Assert.True(string.CompareOrdinal(list[0].Id, list[1].Id) > 0);
    }

    /// <summary>
    /// Test: GET /latest returns the most recently uploaded backup's bytes.
    /// Expectation: Response body equals the uploaded JSON, content-type application/json.
    /// </summary>
    [Fact]
    public async Task RestoreLatest_ReturnsMostRecentBackupBytes()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        var products = SampleProducts();
        var originalBytes = JsonSerializer.SerializeToUtf8Bytes(products);

        var upload = await client.PostAsync($"/api/backup/{accountId}/dev",
            new ByteArrayContent(originalBytes) { Headers = { ContentType = new("application/json") } });
        upload.EnsureSuccessStatusCode();

        var latest = await client.GetAsync($"/api/backup/{accountId}/dev/latest");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal("application/json", latest.Content.Headers.ContentType?.MediaType);
        var returnedBytes = await latest.Content.ReadAsByteArrayAsync();
        Assert.Equal(originalBytes, returnedBytes);
    }

    /// <summary>
    /// Test: GET /latest on a device with no backups.
    /// Expectation: 404 Not Found.
    /// </summary>
    [Fact]
    public async Task RestoreLatest_NoBackups_ReturnsNotFound()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        var response = await client.GetAsync($"/api/backup/{accountId}/nosuchdev/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Test: GET /{backupId} with a non-existent backup id.
    /// Expectation: 404 Not Found.
    /// </summary>
    [Fact]
    public async Task RestoreSpecific_NotFound_Returns404()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        var response = await client.GetAsync($"/api/backup/{accountId}/dev/backup_99999999_000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Test: GET /{accountId}/devices after uploading to two devices.
    /// Expectation: Returns both device names.
    /// </summary>
    [Fact]
    public async Task ListDevices_ReturnsDevicesWithBackups()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);
        await client.PostAsync($"/api/backup/{accountId}/alpha", JsonContent(SampleProducts()));
        await client.PostAsync($"/api/backup/{accountId}/beta", JsonContent(SampleProducts()));

        var resp = await client.GetAsync($"/api/backup/{accountId}/devices");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var devices = await resp.Content.ReadFromJsonAsync<List<string>>(JsonOpts);
        Assert.NotNull(devices);
        Assert.Contains("alpha", devices!);
        Assert.Contains("beta", devices!);
    }

    /// <summary>
    /// Test: Full encrypted round-trip through the server.
    /// Encrypt a product list on the client, upload as application/octet-stream,
    /// download via /latest, decrypt with the same key, and verify the product list
    /// deserializes to the exact original values.
    /// Expectation: Downloaded bytes match uploaded bytes exactly; decrypted plaintext
    /// matches original JSON; server served it with application/octet-stream content type.
    /// </summary>
    [Fact]
    public async Task EncryptedBackup_RoundTripsThroughServer()
    {
        using var factory = new TestServerFactory(acceptPolicy: "encrypted");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);

        // Client-side: encrypt a real product list.
        var originalProducts = new List<Product>
        {
            new Product
            {
                Id = 42, Name = "SecretPill", Quantity = 77, LowLimit = 3,
                Unit = "pill", AmountPerPackage = 30,
                ConsumeByNeed = true, ConsumeByNeedAmount = 2,
                Dosages = new(), History = new()
            }
        };
        var originalJson = JsonSerializer.SerializeToUtf8Bytes(originalProducts);
        var key = BackupCrypto.DeriveEncryptionKey("user-password", accountId);
        var encrypted = BackupCrypto.Encrypt(originalJson, key);

        // Sanity: ciphertext should not contain the plaintext product name.
        Assert.DoesNotContain("SecretPill", Encoding.UTF8.GetString(encrypted));

        // Upload as octet-stream.
        var uploadContent = new ByteArrayContent(encrypted);
        uploadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var upload = await client.PostAsync($"/api/backup/{accountId}/devEnc", uploadContent);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        // Server should have written a .bin file, not .json.
        var devicePath = Path.Combine(factory.StoragePath, accountId, "devEnc");
        Assert.Single(Directory.GetFiles(devicePath, "backup_*.bin"));
        Assert.Empty(Directory.GetFiles(devicePath, "backup_*.json"));

        // Download via /latest.
        var latest = await client.GetAsync($"/api/backup/{accountId}/devEnc/latest");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal("application/octet-stream", latest.Content.Headers.ContentType?.MediaType);
        var downloaded = await latest.Content.ReadAsByteArrayAsync();

        // Bytes must match what we uploaded exactly.
        Assert.Equal(encrypted, downloaded);

        // Decrypt with the same key and deserialize.
        var decrypted = BackupCrypto.Decrypt(downloaded, key);
        Assert.Equal(originalJson, decrypted);

        var roundTripped = JsonSerializer.Deserialize<List<Product>>(decrypted, JsonOpts);
        Assert.NotNull(roundTripped);
        Assert.Single(roundTripped!);
        Assert.Equal(42, roundTripped[0].Id);
        Assert.Equal("SecretPill", roundTripped[0].Name);
        Assert.Equal(77, roundTripped[0].Quantity);
        Assert.True(roundTripped[0].ConsumeByNeed);
        Assert.Equal(2, roundTripped[0].ConsumeByNeedAmount);
    }

    #endregion

    #region Pruning

    /// <summary>
    /// Test: Upload more backups than MaxBackupsPerDevice allows.
    /// Assumptions: Default MaxBackupsPerDevice is 10. Upload 12 with distinct timestamps.
    /// Expectation: Only 10 most recent remain on disk.
    /// </summary>
    [Fact]
    public async Task Upload_ExceedsMaxBackupsPerDevice_OldestAreePruned()
    {
        using var factory = new TestServerFactory(acceptPolicy: "both");
        using var client = factory.CreateClient();

        var accountId = await CreateAccountAsync(client);

        // 12 uploads — filenames include yyyyMMdd_HHmmss, so we need 1s gaps.
        for (var i = 0; i < 12; i++)
        {
            await client.PostAsync($"/api/backup/{accountId}/devP", JsonContent(SampleProducts()));
            if (i < 11) await Task.Delay(1100);
        }

        var devicePath = Path.Combine(factory.StoragePath, accountId, "devP");
        Assert.Equal(10, Directory.GetFiles(devicePath, "backup_*.json").Length);
    }

    #endregion
}
