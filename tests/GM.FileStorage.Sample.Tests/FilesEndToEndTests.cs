using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.FileStorage.Sample.Infrastructure.Jobs;
using GM.FileStorage.Sample.Persistence.Context;
using GM.Testing.AspNetCore;
using GM.Scheduling;
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

    // Keys don't carry the tenant, so this test's files are the ones tagged with its unique tenant id.
    private IQueryable<StoredFile> OurFiles(ApplicationDbContext context) =>
        context.Set<StoredFile>().Where(f => f.Metadata.Any(m => m.Name == StoredFileMetadataNames.TenantId && m.Value == _tenantId));

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
                var entryIds = (await OurCleanupEntriesAsync(context)).Select(x => x.Id).ToList();
                var fileIds = await OurFiles(context).Select(x => x.Id).ToListAsync();
                await context.Set<StoredFileMetadata>().Where(x => fileIds.Contains(x.StoredFileId)).ExecuteDeleteAsync();
                await context.Set<StoredFile>().Where(x => fileIds.Contains(x.Id)).ExecuteDeleteAsync();
                await context.Set<StorageCleanup>().Where(x => entryIds.Contains(x.Id)).ExecuteDeleteAsync();
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
        var original = Encoding.UTF8.GetBytes("%PDF-1.7 passport scan bytes");

        // Upload: a generated key that is just {guid}{ext} (no tenant, user, date or folders), recorded in the catalog.
        var uploaded = await UploadAsync(client, original, "passport.pdf", $"tenantId={_tenantId}&userId=u1");
        Assert.Matches(@"^[0-9a-f]{32}\.pdf$", uploaded.Key);
        Assert.DoesNotContain(_tenantId, uploaded.Key, StringComparison.Ordinal);
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
        var keyStart = uploaded.Key[..8];
        Assert.Single(await ListAsync(client, $"tenantId={_tenantId}&keyPrefix={keyStart}"));
        Assert.Empty(await ListAsync(client, $"tenantId={_tenantId}&keyPrefix=zz"));

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

        // Delete: only marks the row deleted (hidden from list, details, download); the bytes stay for a restore,
        // and nothing is queued for cleanup — even when the job runs.
        var delete = await client.DeleteAsync($"/api/v1/files/{uploaded.Key}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{uploaded.Key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{uploaded.Id}")).StatusCode);
        Assert.Empty(await ListAsync(client, $"tenantId={_tenantId}"));

        var deleted = await client.GetFromJsonAsync<StoredFileResponse>($"/api/v1/files/{uploaded.Id}?visibility=All");
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
        Assert.Empty(await CleanupEntriesAsync());
        await RunCleanupJobAsync();
        Assert.Single(StoredObjects());

        // Restore: back exactly as it was, nothing re-uploaded. Restoring a live file is a 404.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/restore/{uploaded.Key}", null)).StatusCode);
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Key}"));
        Assert.Single(await ListAsync(client, $"tenantId={_tenantId}"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/restore/{uploaded.Key}", null)).StatusCode);

        // Delete again before re-uploading below.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{uploaded.Key}")).StatusCode);

        // Re-upload to the same key: the same row comes back, with the new content and metadata.
        var replacement = Encoding.UTF8.GetBytes("%PDF-1.7 a newer passport scan");
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

        // A re-upload replaces content, so the deleted version's bytes go: only the new version is stored.
        Assert.Single(StoredObjects());

        // Still one row and one metadata row per name for the key.
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await context.Set<StoredFile>().CountAsync(x => x.Key == uploaded.Key));
        Assert.Equal(2, await context.Set<StoredFileMetadata>().CountAsync(x => x.StoredFileId == uploaded.Id));
    }

    [Fact]
    public async Task Replacing_a_file_stores_a_new_version_and_deletes_the_old_one()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var first = await UploadAsync(client, Encoding.UTF8.GetBytes("%PDF-1.7 v1"), "a.pdf", $"tenantId={_tenantId}&userId=u1");
        var firstObject = Assert.Single(StoredObjects());

        var second = await UploadAsync(client, Encoding.UTF8.GetBytes("%PDF-1.7 version two"), "a.pdf",
            $"tenantId={_tenantId}&userId=u1&key={Uri.EscapeDataString(first.Key)}");

        // Same file, new bytes in a new place; the old version is gone and nothing is left queued.
        Assert.Equal(first.Id, second.Id);
        var secondObject = Assert.Single(StoredObjects());
        Assert.NotEqual(firstObject, secondObject);
        Assert.Equal("%PDF-1.7 version two", await client.GetStringAsync($"/api/v1/files/{first.Key}"));
        Assert.Empty(await CleanupEntriesAsync());
    }

    [Fact]
    public async Task Racing_replacements_leave_one_consistent_version()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var original = await UploadAsync(client, Encoding.UTF8.GetBytes("%PDF-1.7 original"), "a.pdf", $"tenantId={_tenantId}&userId=u1");

        var bodies = Enumerable.Range(0, 6).Select(i => $"%PDF-1.7 racer {i}").ToArray();
        var responses = await Task.WhenAll(bodies.Select(body => PostAsync(client, Encoding.UTF8.GetBytes(body), "a.pdf",
            "application/pdf", $"&key={Uri.EscapeDataString(original.Key)}")));

        // Every racer either won (200) or was told to retry (400); none corrupted the file.
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        var current = await client.GetStringAsync($"/api/v1/files/{original.Key}");
        Assert.Contains(current, bodies);

        // Whatever a loser left behind is queued; once the job runs, only the current version's bytes remain.
        await RunCleanupJobAsync();
        Assert.Single(StoredObjects());
        Assert.Equal(current, await client.GetStringAsync($"/api/v1/files/{original.Key}"));
    }

    [Fact]
    public async Task The_cleanup_job_removes_abandoned_bytes_but_keeps_live_ones()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var live = await UploadAsync(client, Encoding.UTF8.GetBytes("%PDF-1.7 live"), "live.pdf", $"tenantId={_tenantId}&userId=u1");

        // An upload that died after storing its bytes but before recording them: bytes plus a pending entry.
        var abandonedKey = $"{_tenantId}/abandoned.pdf~{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            await storage.UploadAsync(new FileUploadRequest(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7 orphan")), "abandoned.pdf", "application/pdf")
            {
                Key = abandonedKey,
            });

            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var liveStorageKey = await context.Set<StoredFile>().Where(x => x.Id == live.Id).Select(x => x.StorageKey).SingleAsync();
            context.Add(StorageCleanup.Create(abandonedKey, DateTime.UtcNow.AddSeconds(-1)));
            // A stray entry for bytes a live file still uses must not delete them.
            context.Add(StorageCleanup.Create(liveStorageKey, DateTime.UtcNow.AddSeconds(-1)));
            await context.SaveChangesAsync();
        }
        Assert.Equal(2, StoredObjects().Length);

        await RunCleanupJobAsync();

        Assert.Single(StoredObjects());
        Assert.Equal("%PDF-1.7 live", await client.GetStringAsync($"/api/v1/files/{live.Key}"));
        Assert.Empty(await CleanupEntriesAsync());
    }

    // The data files in this test's own storage root (not the local provider's .gmmeta sidecars).
    private string[] StoredObjects() =>
        Directory.Exists(_root)
            ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".gmmeta", StringComparison.Ordinal)).ToArray()
            : [];

    private async Task<List<StorageCleanup>> CleanupEntriesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await OurCleanupEntriesAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    // Entries for this test's files: every version is stored as {key}~{version}; hand-made ones start with the
    // tenant id. Filtered in memory — the table is tiny in tests.
    private async Task<List<StorageCleanup>> OurCleanupEntriesAsync(ApplicationDbContext context)
    {
        var keys = await OurFiles(context).Select(x => x.Key).ToListAsync();
        return (await context.Set<StorageCleanup>().ToListAsync())
            .Where(x => x.StorageKey.StartsWith(_tenantId, StringComparison.Ordinal)
                        || keys.Any(k => x.StorageKey.StartsWith(k + "~", StringComparison.Ordinal)))
            .ToList();
    }

    // Makes this test's queued entries due and runs the job once, as the scheduler would.
    private async Task RunCleanupJobAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ids = (await OurCleanupEntriesAsync(context)).Select(x => x.Id).ToList();
            await context.Set<StorageCleanup>().Where(x => ids.Contains(x.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DueAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        }

        var job = new StorageCleanupJob(_factory.Services.GetRequiredService<IServiceScopeFactory>());
        await job.ExecuteAsync(new JobExecutionContext { JobName = StorageCleanupJob.JobName, FireTimeUtc = DateTimeOffset.UtcNow }, default);
    }

    [Fact]
    public async Task Key_routes_accept_a_url_encoded_key_as_swagger_sends_it()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var original = Encoding.UTF8.GetBytes("%PDF-1.7 encoded key");
        // Generated keys have no slashes, but explicit ones may: upload to one that does.
        var uploaded = await UploadAsync(client, original, "e.pdf",
            $"tenantId={_tenantId}&userId=u1&key={Uri.EscapeDataString($"{_tenantId}/docs/e.pdf")}");
        var encoded = Uri.EscapeDataString(uploaded.Key); // {tenant}%2Fdocs%2Fe.pdf
        Assert.Contains("%2F", encoded, StringComparison.Ordinal);

        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{encoded}"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/presign/{encoded}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{encoded}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/restore/{encoded}", null)).StatusCode);
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Key}"));
    }

    [Fact]
    public async Task Download_and_delete_of_an_unknown_key_return_404()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var key = $"{_tenantId}/does-not-exist.bin";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/files/{key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/presign/{key}")).StatusCode);
    }

    [Fact]
    public async Task Download_honours_range_requests()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = _factory.CreateClient();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 0123456789"); // 19 bytes
        var uploaded = await UploadAsync(client, bytes, "video-ish.pdf", $"tenantId={_tenantId}&userId=u1");

        async Task<HttpResponseMessage> GetRange(string range)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/{uploaded.Key}");
            request.Headers.Range = RangeHeaderValue.Parse(range);
            return await client.SendAsync(request);
        }

        var middle = await GetRange("bytes=9-12");
        Assert.Equal(HttpStatusCode.PartialContent, middle.StatusCode);
        Assert.Equal("bytes 9-12/19", middle.Content.Headers.ContentRange!.ToString());
        Assert.Equal("0123", await middle.Content.ReadAsStringAsync());

        var tail = await GetRange("bytes=-3");
        Assert.Equal("bytes 16-18/19", tail.Content.Headers.ContentRange!.ToString());
        Assert.Equal("789", await tail.Content.ReadAsStringAsync());

        var pastTheEnd = await GetRange("bytes=19-");
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, pastTheEnd.StatusCode);
        Assert.Equal("bytes */19", pastTheEnd.Content.Headers.ContentRange!.ToString());

        var whole = await client.GetAsync($"/api/v1/files/{uploaded.Key}");
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Contains("bytes", whole.Headers.AcceptRanges);
    }

    [Fact]
    public async Task Upload_rejects_files_that_break_the_upload_rules()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = _factory.CreateClient();

        // An executable dressed up as a PDF: the bytes give it away (FileStorage:VerifyContentType).
        var disguised = await PostAsync(client, [(byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0], "invoice.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.BadRequest, disguised.StatusCode);
        Assert.Contains("application/vnd.microsoft.portable-executable", await disguised.Content.ReadAsStringAsync());

        // A type outside FileStorage:AllowedContentTypes.
        var html = await PostAsync(client, Encoding.UTF8.GetBytes("<html></html>"), "page.html", "text/html");
        Assert.Equal(HttpStatusCode.BadRequest, html.StatusCode);

        Assert.Empty(await ListAsync(client, $"tenantId={_tenantId}"));
    }

    private async Task<HttpResponseMessage> PostAsync(HttpClient client, byte[] bytes, string fileName, string contentType, string extraQuery = "")
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        return await client.PostAsync($"/api/v1/files?tenantId={_tenantId}&userId=u1{extraQuery}", form);
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
