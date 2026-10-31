using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.FileStorage.Sample.Persistence.Context;
using GM.Testing.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Tests;

/// <summary>
/// End-to-end proof of the file catalog over HTTP: an upload is stored and recorded (with its tenant/user metadata),
/// is listed by tenant and readable by id, downloads byte-for-byte, and on delete loses its bytes while its row is
/// soft-deleted; uploading to the same key again restores that row with the new content. Runs against a throwaway
/// local storage root. Requires Postgres (ConnectionStrings:ApplicationDatabase); no-ops if it isn't reachable.
/// </summary>
public sealed class FilesEndToEndTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gm-fs-sample-tests", Guid.NewGuid().ToString("N"));
    private readonly string _tenantId = $"t-{Guid.NewGuid():N}";
    private readonly GmWebApplicationFactory<Program> _factory;
    private bool _infraReady;

    public FilesEndToEndTests() =>
        _factory = new GmWebApplicationFactory<Program>().WithConfig("FileStorage:Local:RootPath", _root);

    private string KeyPrefix => $"tenants/{_tenantId}/";

    public async Task InitializeAsync()
    {
        try
        {
            // Booting the host applies the migrations (see Program); then check the catalog is really there.
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
                await context.Set<StoredFileMetadata>()
                    .Where(x => x.StoredFile.Key.StartsWith(KeyPrefix)).ExecuteDeleteAsync();
                await context.Set<StoredFile>().Where(x => x.Key.StartsWith(KeyPrefix)).ExecuteDeleteAsync();
            }
            catch { /* best-effort cleanup */ }
        }
        await _factory.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Upload_list_details_download_delete_and_reupload_restores_the_row()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = _factory.CreateClient();
        var original = Encoding.UTF8.GetBytes("passport scan bytes");

        // Upload: a tenant/user-scoped key, recorded in the catalog.
        var uploaded = await UploadAsync(client, original, "passport.pdf", $"tenantId={_tenantId}&userId=u1");
        Assert.StartsWith($"tenants/{_tenantId}/users/u1/", uploaded.Key, StringComparison.Ordinal);
        Assert.Equal(original.Length, uploaded.Size);
        Assert.NotEqual(Guid.Empty, uploaded.Id);

        // List by tenant: exactly this file, with the metadata it was uploaded with.
        var listed = Assert.Single(await ListAsync(client, $"tenantId={_tenantId}"));
        Assert.Equal(uploaded.Id, listed.Id);
        Assert.Equal(uploaded.Key, listed.Key);
        Assert.Equal(_tenantId, listed.Metadata!["tenantId"]);
        Assert.Equal("u1", listed.Metadata!["userId"]);
        Assert.Single(await ListAsync(client, $"tenantId={_tenantId}&userId=u1"));
        Assert.Empty(await ListAsync(client, $"tenantId={_tenantId}&userId=someone-else"));
        Assert.Single(await ListAsync(client, $"keyPrefix={Uri.EscapeDataString(KeyPrefix)}"));

        // Details by id.
        var details = await client.GetFromJsonAsync<StoredFileResponse>($"/api/v1/files/{uploaded.Id}");
        Assert.NotNull(details);
        Assert.Equal("passport.pdf", details.FileName);
        Assert.Equal("application/pdf", details.ContentType);
        Assert.Equal(original.Length, details.Size);
        Assert.False(details.IsDeleted);

        // Download: byte-for-byte; presign: a link.
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Key}"));
        var presign = await client.GetAsync($"/api/v1/presign/{uploaded.Key}");
        Assert.Equal(HttpStatusCode.OK, presign.StatusCode);

        // Delete: the bytes go, the row stays soft-deleted (hidden from list, details, download).
        var delete = await client.DeleteAsync($"/api/v1/files/{uploaded.Key}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{uploaded.Key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{uploaded.Id}")).StatusCode);
        Assert.Empty(await ListAsync(client, $"tenantId={_tenantId}"));

        var deleted = await client.GetFromJsonAsync<StoredFileResponse>($"/api/v1/files/{uploaded.Id}?visibility=All");
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
        Assert.False(File.Exists(Path.Combine(_root, uploaded.Key)));

        // Re-upload to the same key: the same row comes back, with the new content and metadata.
        var replacement = Encoding.UTF8.GetBytes("a newer passport scan");
        var reuploaded = await UploadAsync(client, replacement, "passport-v2.pdf",
            $"tenantId={_tenantId}&userId=u2&key={Uri.EscapeDataString(uploaded.Key)}");
        Assert.Equal(uploaded.Id, reuploaded.Id);
        Assert.Equal(uploaded.Key, reuploaded.Key);

        var restored = Assert.Single(await ListAsync(client, $"tenantId={_tenantId}"));
        Assert.Equal(uploaded.Id, restored.Id);
        Assert.False(restored.IsDeleted);
        Assert.Equal("passport-v2.pdf", restored.FileName);
        Assert.Equal(replacement.Length, restored.Size);
        Assert.Equal("u2", restored.Metadata!["userId"]);
        Assert.Equal(replacement, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Key}"));

        // Still one row and one metadata row per name for the key.
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await context.Set<StoredFile>().CountAsync(x => x.Key == uploaded.Key));
        Assert.Equal(2, await context.Set<StoredFileMetadata>().CountAsync(x => x.StoredFileId == uploaded.Id));
    }

    [Fact]
    public async Task Download_and_delete_of_an_unknown_key_return_404()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var key = $"{KeyPrefix}users/u1/does-not-exist.bin";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/files/{key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/presign/{key}")).StatusCode);
    }

    private static async Task<UploadResponse> UploadAsync(HttpClient client, byte[] bytes, string fileName, string query)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", fileName);

        var response = await client.PostAsync($"/api/v1/files?{query}", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.NotNull(result);
        return result;
    }

    private static async Task<List<StoredFileResponse>> ListAsync(HttpClient client, string query)
    {
        var result = await client.GetFromJsonAsync<List<StoredFileResponse>>($"/api/v1/files?{query}");
        Assert.NotNull(result);
        return result;
    }

    private sealed record UploadResponse(Guid Id, string Key, string FileName, string ContentType, long Size, string? Checksum);

    private sealed record StoredFileResponse(
        Guid Id,
        string Key,
        string FileName,
        string ContentType,
        long Size,
        bool IsDeleted,
        Dictionary<string, string>? Metadata);
}
