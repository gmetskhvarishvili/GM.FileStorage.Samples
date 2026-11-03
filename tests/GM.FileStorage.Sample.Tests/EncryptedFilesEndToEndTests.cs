using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.FileStorage.Sample.Persistence.Context;
using GM.Testing.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Tests;

/// <summary>
/// The sample with encryption at rest switched on (a master key under FileStorage:Encryption): the bytes on disk are
/// ciphertext, while uploads, downloads and ranges over HTTP behave exactly as without it, and presign is refused.
/// Requires Postgres; no-ops if it isn't reachable.
/// </summary>
public sealed class EncryptedFilesEndToEndTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gm-fs-sample-encrypted", Guid.NewGuid().ToString("N"));
    private readonly string _tenantId = $"t-{Guid.NewGuid():N}";
    private readonly GmWebApplicationFactory<Program> _factory;
    private bool _infraReady;

    public EncryptedFilesEndToEndTests() =>
        _factory = new GmWebApplicationFactory<Program>()
            .WithConfig("FileStorage:Local:RootPath", _root)
            .WithConfig("FileStorage:Encryption:Enabled", "true")
            .WithConfig("FileStorage:Encryption:ActiveKeyId", "test")
            .WithConfig("FileStorage:Encryption:Keys:test", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    private string KeyPrefix => $"tenants/{_tenantId}/";

    public async Task InitializeAsync()
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            _infraReady = await context.Database.CanConnectAsync()
                          && !(await context.Database.GetPendingMigrationsAsync()).Any();
        }
        catch
        {
            _infraReady = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_infraReady)
        {
            try
            {
                using var scope = _factory.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Set<StoredFileMetadata>().Where(x => x.StoredFile.Key.StartsWith(KeyPrefix)).ExecuteDeleteAsync();
                await context.Set<StoredFile>().Where(x => x.Key.StartsWith(KeyPrefix)).ExecuteDeleteAsync();
            }
            catch { /* best-effort cleanup */ }
        }
        await _factory.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Files_are_encrypted_at_rest_but_behave_the_same_over_HTTP()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var original = Encoding.UTF8.GetBytes("%PDF-1.7 passport number X1234567");

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(original);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "passport.pdf");
        var response = await client.PostAsync($"/api/v1/files?tenantId={_tenantId}&userId=u1", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var uploaded = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.NotNull(uploaded);
        Assert.Equal(original.Length, uploaded.Size); // the plaintext's size, not the ciphertext's

        // On disk: ciphertext only.
        var stored = Directory.GetFiles(Path.Combine(_root, KeyPrefix), "*", SearchOption.AllDirectories)
            .Single(f => !f.EndsWith(".gmmeta", StringComparison.Ordinal));
        var storedBytes = await File.ReadAllBytesAsync(stored);
        Assert.StartsWith("GMFE", Encoding.ASCII.GetString(storedBytes, 0, 4));
        Assert.DoesNotContain("X1234567", Encoding.UTF8.GetString(storedBytes), StringComparison.Ordinal);

        // Over HTTP: the original bytes, ranges included.
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Key}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/{uploaded.Key}");
        request.Headers.Range = new RangeHeaderValue(25, 32);
        var range = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Equal("X1234567", await range.Content.ReadAsStringAsync());

        // A presigned link would serve ciphertext, so it's refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/presign/{uploaded.Key}")).StatusCode);
    }

    private sealed record UploadResponse(Guid Id, string Key, string FileName, string ContentType, long Size, string? Checksum);
}
