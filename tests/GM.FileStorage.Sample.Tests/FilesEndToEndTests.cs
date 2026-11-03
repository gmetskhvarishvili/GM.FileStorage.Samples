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
/// End-to-end proof of the file catalog over HTTP: an upload gets a GUID and a visible storage location, is listed by
/// tenant and readable by id, downloads byte-for-byte, can be deleted (bytes kept), restored and replaced. Runs against
/// a throwaway local storage root, with a key template prefixed by this test's tenant id so its stored objects and
/// cleanup entries are easy to tell apart. Requires Postgres (ConnectionStrings:ApplicationDatabase); no-ops if it
/// isn't reachable.
/// </summary>
public sealed class FilesEndToEndTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gm-fs-sample-tests", Guid.NewGuid().ToString("N"));
    private readonly string _tenantId = $"t-{Guid.NewGuid():N}";
    private readonly GmWebApplicationFactory<Program> _factory;
    private bool _infraReady;

    public FilesEndToEndTests() =>
        _factory = new GmWebApplicationFactory<Program>()
            .WithConfig("FileStorage:Local:RootPath", _root)
            // The production layout, under a per-test folder: {tenant}/{category}/{shard}/{id}{ext}.
            .WithConfig("FileStorage:KeyTemplate", $"{_tenantId}/{{category}}/{{shard}}/{{id}}{{ext}}");

    // This test's files: the ones tagged with its unique tenant id.
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
                var fileIds = await OurFiles(context).Select(x => x.Id).ToListAsync();
                await context.Set<StoredFileMetadata>().Where(x => fileIds.Contains(x.StoredFileId)).ExecuteDeleteAsync();
                await context.Set<StoredFile>().Where(x => fileIds.Contains(x.Id)).ExecuteDeleteAsync();
                await context.Set<StorageCleanup>().Where(x => x.StorageKey.StartsWith(_tenantId)).ExecuteDeleteAsync();
            }
            catch { /* best-effort cleanup */ }
        }
        await _factory.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Upload_list_details_download_delete_restore_and_replace()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = _factory.CreateClient();
        var original = Encoding.UTF8.GetBytes("%PDF-1.7 passport scan bytes");

        // Upload: the file is a GUID; where its bytes went is visible — folders from the key template.
        var uploaded = await UploadAsync(client, original, "passport.pdf", "userId=u1");
        Assert.NotEqual(Guid.Empty, uploaded.Id);
        // Callers see just the stored name; inside, it sits in {category}/{shard}/ folders (the shard is the name's
        // first two characters), which the catalog keeps.
        Assert.Matches(@"^[0-9a-f]{32}\.pdf$", uploaded.StorageKey);
        var path = await StoragePathAsync(uploaded.Id);
        Assert.Equal($"{_tenantId}/documents/{uploaded.StorageKey[..2]}/{uploaded.StorageKey}", path);
        Assert.True(File.Exists(Path.Combine(_root, path)));
        Assert.Equal(original.Length, uploaded.Size);

        // List by tenant: exactly this file, with its storage location and metadata.
        var listed = Assert.Single(await ListAsync(client, $"tenantId={_tenantId}"));
        Assert.Equal((uploaded.Id, uploaded.StorageKey), (listed.Id, listed.StorageKey));
        Assert.Equal(_tenantId, listed.Metadata!["tenantId"]);
        Assert.Equal("u1", listed.Metadata!["userId"]);
        Assert.Single(await ListAsync(client, $"tenantId={_tenantId}&userId=u1"));
        Assert.Empty(await ListAsync(client, $"tenantId={_tenantId}&userId=someone-else"));
        // Storage folders double as a filter.
        Assert.Single(await ListAsync(client, $"folder={Uri.EscapeDataString($"{_tenantId}/documents/")}"));
        Assert.Empty(await ListAsync(client, $"folder={Uri.EscapeDataString($"{_tenantId}/images/")}"));

        // Details by id.
        var details = await client.GetFromJsonAsync<StoredFileResponse>($"/api/v1/files/{uploaded.Id}");
        Assert.NotNull(details);
        Assert.Equal(("passport.pdf", "application/pdf", uploaded.StorageKey), (details.FileName, details.ContentType, details.StorageKey));
        Assert.False(details.IsDeleted);

        // Download: byte-for-byte; presign: a link.
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Id}/content"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/files/{uploaded.Id}/presign")).StatusCode);

        // Delete: only marks the row deleted (hidden from list, details, download); the bytes stay for a restore,
        // and nothing is queued for cleanup — even when the job runs.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{uploaded.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{uploaded.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{uploaded.Id}")).StatusCode);
        Assert.Empty(await ListAsync(client, $"tenantId={_tenantId}"));
        var deleted = await client.GetFromJsonAsync<StoredFileResponse>($"/api/v1/files/{uploaded.Id}?visibility=All");
        Assert.True(deleted!.IsDeleted);
        Assert.Empty(await CleanupEntriesAsync());
        await RunCleanupJobAsync();
        Assert.Single(StoredObjects());

        // Restore: back exactly as it was, nothing re-uploaded. Restoring a live file is a 404.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/files/{uploaded.Id}/restore", null)).StatusCode);
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Id}/content"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/files/{uploaded.Id}/restore", null)).StatusCode);

        // Replace a deleted file's content: same id, restored, new content and metadata, new storage location.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{uploaded.Id}")).StatusCode);
        var replacement = Encoding.UTF8.GetBytes("%PDF-1.7 a newer passport scan");
        var replaced = await ReplaceAsync(client, uploaded.Id, replacement, "passport-v2.pdf", "userId=u2");
        Assert.Equal(uploaded.Id, replaced.Id);
        Assert.NotEqual(uploaded.StorageKey, replaced.StorageKey);

        var restored = Assert.Single(await ListAsync(client, $"tenantId={_tenantId}"));
        Assert.Equal((uploaded.Id, false, "passport-v2.pdf", "u2"), (restored.Id, restored.IsDeleted, restored.FileName, restored.Metadata!["userId"]));
        Assert.Equal(replacement, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Id}/content"));

        // The old version's bytes are gone: only the new location is stored. Still one row, one metadata row per name.
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, await StoragePathAsync(replaced.Id))), Path.GetFullPath(Assert.Single(StoredObjects())), ignoreCase: true);
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await context.Set<StoredFile>().CountAsync(x => x.Id == uploaded.Id));
        Assert.Equal(2, await context.Set<StoredFileMetadata>().CountAsync(x => x.StoredFileId == uploaded.Id));
    }

    [Fact]
    public async Task Racing_replacements_leave_one_consistent_version()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var original = await UploadAsync(client, Encoding.UTF8.GetBytes("%PDF-1.7 original"), "a.pdf", "userId=u1");

        var bodies = Enumerable.Range(0, 6).Select(i => $"%PDF-1.7 racer {i}").ToArray();
        var responses = await Task.WhenAll(bodies.Select(body =>
            SendFileAsync(client, HttpMethod.Put, $"/api/v1/files/{original.Id}/content", Encoding.UTF8.GetBytes(body), "a.pdf", "application/pdf")));

        // Every racer either won (200) or was told to retry (400); none corrupted the file.
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        var current = await client.GetStringAsync($"/api/v1/files/{original.Id}/content");
        Assert.Contains(current, bodies);

        // Whatever a loser left behind is queued; once the job runs, only the current version's bytes remain.
        await RunCleanupJobAsync();
        Assert.Single(StoredObjects());
        Assert.Equal(current, await client.GetStringAsync($"/api/v1/files/{original.Id}/content"));
    }

    [Fact]
    public async Task The_cleanup_job_removes_abandoned_bytes_but_keeps_live_ones()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var live = await UploadAsync(client, Encoding.UTF8.GetBytes("%PDF-1.7 live"), "live.pdf", "userId=u1");

        // An upload that died after storing its bytes but before recording them: bytes plus a pending entry.
        var abandonedKey = $"{_tenantId}/documents/ab/abandoned.pdf";
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            await storage.UploadAsync(new FileUploadRequest(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7 orphan")), "abandoned.pdf", "application/pdf")
            {
                Key = abandonedKey,
            });

            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Add(StorageCleanup.Create(abandonedKey, DateTime.UtcNow.AddSeconds(-1)));
            // A stray entry for bytes a live file still uses must not delete them.
            context.Add(StorageCleanup.Create(await StoragePathAsync(live.Id), DateTime.UtcNow.AddSeconds(-1)));
            await context.SaveChangesAsync();
        }
        Assert.Equal(2, StoredObjects().Length);

        await RunCleanupJobAsync();

        Assert.Single(StoredObjects());
        Assert.Equal("%PDF-1.7 live", await client.GetStringAsync($"/api/v1/files/{live.Id}/content"));
        Assert.Empty(await CleanupEntriesAsync());
    }

    [Fact]
    public async Task Unknown_ids_return_404()
    {
        if (!_infraReady) return;

        using var client = _factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{id}/presign")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/files/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/files/{id}/restore", null)).StatusCode);

        // Replacing an unknown file is refused before any bytes are stored.
        var replace = await SendFileAsync(client, HttpMethod.Put, $"/api/v1/files/{id}/content",
            Encoding.UTF8.GetBytes("%PDF-1.7 x"), "x.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.NotFound, replace.StatusCode);
        Assert.Empty(StoredObjects());
    }

    [Fact]
    public async Task Download_honours_range_requests()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = _factory.CreateClient();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 0123456789"); // 19 bytes
        var uploaded = await UploadAsync(client, bytes, "video-ish.pdf", "userId=u1");
        var url = $"/api/v1/files/{uploaded.Id}/content";

        async Task<HttpResponseMessage> GetRange(string range)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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

        var whole = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Contains("bytes", whole.Headers.AcceptRanges);
    }

    [Fact]
    public async Task Upload_rejects_files_that_break_the_upload_rules()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = _factory.CreateClient();
        var url = $"/api/v1/files?tenantId={_tenantId}&userId=u1";

        // An executable dressed up as a PDF: the bytes give it away (FileStorage:VerifyContentType).
        var disguised = await SendFileAsync(client, HttpMethod.Post, url, [(byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0], "invoice.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.BadRequest, disguised.StatusCode);
        Assert.Contains("application/vnd.microsoft.portable-executable", await disguised.Content.ReadAsStringAsync());

        // A type outside FileStorage:AllowedContentTypes.
        var html = await SendFileAsync(client, HttpMethod.Post, url, Encoding.UTF8.GetBytes("<html></html>"), "page.html", "text/html");
        Assert.Equal(HttpStatusCode.BadRequest, html.StatusCode);

        Assert.Empty(await ListAsync(client, $"tenantId={_tenantId}"));
        Assert.Empty(StoredObjects());
    }

    // Where a file's bytes really are (the full storage key, folders included) — kept by the catalog, not shown.
    private async Task<string> StoragePathAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Set<StoredFile>().Where(x => x.Id == id).Select(x => x.StorageKey).SingleAsync();
    }

    // The data files in this test's own storage root (not the local provider's .gmmeta sidecars).
    private string[] StoredObjects() =>
        Directory.Exists(_root)
            ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".gmmeta", StringComparison.Ordinal)).ToArray()
            : [];

    // This test's cleanup entries: all its storage keys start with its tenant id (see the key template above).
    private async Task<List<StorageCleanup>> CleanupEntriesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Set<StorageCleanup>().Where(x => x.StorageKey.StartsWith(_tenantId)).ToListAsync();
    }

    // Makes this test's queued entries due and runs the job once, as the scheduler would.
    private async Task RunCleanupJobAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Set<StorageCleanup>().Where(x => x.StorageKey.StartsWith(_tenantId))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DueAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        }

        var job = new StorageCleanupJob(_factory.Services.GetRequiredService<IServiceScopeFactory>());
        await job.ExecuteAsync(new JobExecutionContext { JobName = StorageCleanupJob.JobName, FireTimeUtc = DateTimeOffset.UtcNow }, default);
    }

    private Task<UploadResponse> UploadAsync(HttpClient client, byte[] bytes, string fileName, string query) =>
        ExpectOkAsync(SendFileAsync(client, HttpMethod.Post, $"/api/v1/files?tenantId={_tenantId}&{query}", bytes, fileName, "application/pdf"));

    private Task<UploadResponse> ReplaceAsync(HttpClient client, Guid id, byte[] bytes, string fileName, string query) =>
        ExpectOkAsync(SendFileAsync(client, HttpMethod.Put, $"/api/v1/files/{id}/content?tenantId={_tenantId}&{query}", bytes, fileName, "application/pdf"));

    private static async Task<UploadResponse> ExpectOkAsync(Task<HttpResponseMessage> send)
    {
        var response = await send;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.NotNull(result);
        return result;
    }

    private static async Task<HttpResponseMessage> SendFileAsync(
        HttpClient client, HttpMethod method, string url, byte[] bytes, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        using var request = new HttpRequestMessage(method, url) { Content = form };
        return await client.SendAsync(request);
    }

    private static async Task<List<StoredFileResponse>> ListAsync(HttpClient client, string query)
    {
        var result = await client.GetFromJsonAsync<List<StoredFileResponse>>($"/api/v1/files?{query}");
        Assert.NotNull(result);
        return result;
    }

    private sealed record UploadResponse(Guid Id, string StorageKey, string FileName, string ContentType, long Size, string? Checksum);

    private sealed record StoredFileResponse(
        Guid Id,
        string StorageKey,
        string FileName,
        string ContentType,
        long Size,
        bool IsDeleted,
        Dictionary<string, string>? Metadata);
}
