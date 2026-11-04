using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.FileStorage.Sample.Infrastructure.Jobs;
using GM.FileStorage.Sample.Persistence.Context;
using GM.Testing.AspNetCore;
using GM.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Tests;

/// <summary>
/// End-to-end proof of the file catalog over HTTP: an upload gets a GUID and a visible storage location, is listed by
/// tenant and readable by id, downloads byte-for-byte, can be deleted (bytes kept) and restored, and its content can't
/// be replaced. Runs against a throwaway local storage root, with a key template prefixed by this test's tenant id so its stored objects and
/// cleanup entries are easy to tell apart. Requires Postgres (ConnectionStrings:ApplicationDatabase); no-ops if it
/// isn't reachable.
/// </summary>
public sealed class FilesEndToEndTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gm-fs-sample-tests", Guid.NewGuid().ToString("N"));
    // This test's tenant and user, sent on every request as GM's actor headers (X-Tenant-Id, X-User-Id).
    private readonly string _tenantId = Guid.NewGuid().ToString();
    private readonly string _userId = Guid.NewGuid().ToString();
    private readonly GmWebApplicationFactory<Program> _factory;
    private bool _infraReady;

    public FilesEndToEndTests() =>
        _factory = new GmWebApplicationFactory<Program>()
            .WithConfig("FileStorage:Local:RootPath", _root)
            // The production layout, under a per-test folder: {tenant}/{category}/{shard}/{id}{ext}.
            .WithConfig("FileStorage:KeyTemplate", $"{_tenantId}/{{category}}/{{shard}}/{{id}}{{ext}}");

    // A client acting for this test's tenant and user, as the gateway would forward them.
    private HttpClient Client(GmWebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", _tenantId);
        client.DefaultRequestHeaders.Add("X-User-Id", _userId);
        return client;
    }

    // This test's files: stored under its own key prefix, whichever tenant they were uploaded for.
    private IQueryable<StoredFile> OurFiles(ApplicationDbContext context) =>
        context.Set<StoredFile>().Where(f => f.StorageKey.StartsWith(_tenantId));

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
    public async Task Upload_list_details_download_delete_and_restore()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = Client(_factory);
        var original = Encoding.UTF8.GetBytes("%PDF-1.7 passport scan bytes");

        // Upload: the file is a GUID, and that's all callers get. Inside, its bytes sit under {category}/{shard}/
        // folders from the key template, named after that id (the shard is its first two characters).
        var uploaded = await UploadAsync(client, original, "passport.pdf");
        Assert.NotEqual(Guid.Empty, uploaded.Id);
        var hex = uploaded.Id.ToString("N");
        var path = await StoragePathAsync(uploaded.Id);
        Assert.Equal($"{_tenantId}/documents/{hex[..2]}/{hex}.pdf", path);
        Assert.True(File.Exists(Path.Combine(_root, path)));
        Assert.Equal(original.Length, uploaded.Size);

        // List by tenant: exactly this file, with its metadata and nothing of where it's stored.
        var listed = Assert.Single(await ListAsync(client, ""));
        Assert.Equal(uploaded.Id, listed.Id);
        Assert.DoesNotContain("storageKey", await client.GetStringAsync("/api/v1/files"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_tenantId, listed.Metadata!["tenantId"]);
        Assert.Equal(_userId, listed.Metadata!["userId"]);
        Assert.Single(await ListAsync(client, $"userId={_userId}"));
        Assert.Empty(await ListAsync(client, $"userId={Guid.NewGuid()}"));

        // Details by id.
        var details = await client.GetFromJsonAsync<StoredFileResponse>($"/api/v1/files/{uploaded.Id}");
        Assert.NotNull(details);
        Assert.Equal(("passport.pdf", "application/pdf"), (details.FileName, details.ContentType));
        Assert.False(details.IsDeleted);

        // Download: byte-for-byte; presign: a link.
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Id}/content"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/files/{uploaded.Id}/presign")).StatusCode);

        // Delete: only marks the row deleted (hidden from list, details, download); the bytes stay for a restore,
        // and nothing is queued for cleanup — even when the job runs.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{uploaded.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{uploaded.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{uploaded.Id}")).StatusCode);
        Assert.Empty(await ListAsync(client, ""));
        var deleted = await client.GetFromJsonAsync<StoredFileResponse>($"/api/v1/files/{uploaded.Id}?visibility=All");
        Assert.True(deleted!.IsDeleted);
        Assert.Empty(await CleanupEntriesAsync());
        await RunCleanupJobAsync();
        Assert.Single(StoredObjects());

        // Restore: back exactly as it was, nothing re-uploaded. Restoring a live file is a 404.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/files/{uploaded.Id}/restore", null)).StatusCode);
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Id}/content"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/files/{uploaded.Id}/restore", null)).StatusCode);

        // Still one row, one metadata row per name, one stored object, and nothing queued.
        Assert.Single(StoredObjects());
        Assert.Empty(await CleanupEntriesAsync());
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await context.Set<StoredFile>().CountAsync(x => x.Id == uploaded.Id));
        Assert.Equal(2, await context.Set<StoredFileMetadata>().CountAsync(x => x.StoredFileId == uploaded.Id));
    }

    [Fact]
    public async Task A_files_content_cannot_be_replaced()
    {
        if (!_infraReady) return;

        using var client = Client(_factory);
        var original = Encoding.UTF8.GetBytes("%PDF-1.7 original");
        var uploaded = await UploadAsync(client, original, "a.pdf");

        // There is no replace endpoint: PUT on the content is refused and nothing is stored.
        var put = await SendFileAsync(client, HttpMethod.Put, $"/api/v1/files/{uploaded.Id}/content",
            Encoding.UTF8.GetBytes("%PDF-1.7 other"), "a.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);

        Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Id}/content"));
        Assert.Single(StoredObjects());
        Assert.Empty(await CleanupEntriesAsync());
    }

    [Fact]
    public async Task The_cleanup_job_removes_abandoned_bytes_but_keeps_live_ones()
    {
        if (!_infraReady) return;

        using var client = Client(_factory);
        var live = await UploadAsync(client, Encoding.UTF8.GetBytes("%PDF-1.7 live"), "live.pdf");

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

        using var client = Client(_factory);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{id}/presign")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/files/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/files/{id}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task Files_belong_to_the_callers_tenant()
    {
        if (!_infraReady) return;

        using var client = Client(_factory);
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 ours");
        var ours = await UploadAsync(client, bytes, "ours.pdf");

        // Another tenant sees nothing of ours: not in its list or usage, and every id is a 404, as if unknown.
        using var other = _factory.CreateClient();
        other.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        Assert.Empty(await ListAsync(other, ""));
        Assert.Equal(0, (await other.GetFromJsonAsync<UsageResponse>("/api/v1/files/usage"))!.FileCount);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/files/{ours.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/files/{ours.Id}?visibility=All")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/files/{ours.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/files/{ours.Id}/presign")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/files/{ours.Id}")).StatusCode);

        // Nor can it restore what we deleted.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{ours.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/v1/files/{ours.Id}/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/files/{ours.Id}/restore", null)).StatusCode);

        // Without X-Tenant-Id the caller works in the shared "public" space: no tenant sees its files, nor it theirs.
        // (The shared space may hold other files on this database, so it's checked by id.)
        using var nobody = _factory.CreateClient();
        var shared = await UploadAsync(nobody, bytes, "shared.pdf");
        Assert.Equal(("public", "anon"), (await nobody.GetFromJsonAsync<StoredFileResponse>($"/api/v1/files/{shared.Id}")) is { } s
            ? (s.Metadata!["tenantId"], s.Metadata!["userId"]) : default);
        Assert.Single(await ListAsync(nobody, $"id={shared.Id}"));
        Assert.Equal(HttpStatusCode.OK,
            (await SendFilesAsync(nobody, "/api/v1/files/batch", ("a.pdf", "application/pdf", bytes))).StatusCode);
        Assert.Empty(await ListAsync(client, $"id={shared.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{shared.Id}/content")).StatusCode);
        Assert.Empty(await ListAsync(nobody, $"id={ours.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await nobody.GetAsync($"/api/v1/files/{ours.Id}")).StatusCode);

        // Ours is untouched.
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/v1/files/{ours.Id}/content"));
        Assert.Single(await ListAsync(client, ""));
    }

    [Fact]
    public async Task Download_honours_range_requests()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = Client(_factory);
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 0123456789"); // 19 bytes
        var uploaded = await UploadAsync(client, bytes, "video-ish.pdf");
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

    [Theory]
    // .docx is a ZIP package; .doc is an OLE compound file. Only the leading bytes are checked.
    [InlineData("report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", new byte[] { (byte)'P', (byte)'K', 3, 4, 20, 0, 6, 0 })]
    [InlineData("report.doc", "application/msword", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })]
    public async Task Upload_accepts_word_documents(string fileName, string contentType, byte[] bytes)
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = Client(_factory);
        var response = await SendFileAsync(client, HttpMethod.Post, $"/api/v1/files",
            bytes, fileName, contentType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var uploaded = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.Equal(contentType, uploaded!.ContentType);
        Assert.StartsWith($"{_tenantId}/documents/", await StoragePathAsync(uploaded.Id));
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Id}/content"));
    }

    [Fact]
    public async Task Presigned_links_serve_the_file_until_altered_or_deleted()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = Client(_factory);
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 shared by link");
        var uploaded = await UploadAsync(client, bytes, "shared.pdf");

        var presigned = await client.GetFromJsonAsync<PresignedResponse>($"/api/v1/files/{uploaded.Id}/presign");
        Assert.NotNull(presigned);
        // The link points at the gateway (PublicBaseUrl); its path and query are what reach this API.
        var link = presigned.Url.PathAndQuery;
        Assert.StartsWith($"/files/{_tenantId}/documents/", link);
        Assert.Contains("&sig=", link);

        // Anyone holding the link gets the file, no other credentials needed.
        var served = await client.GetAsync(link);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(bytes, await served.Content.ReadAsByteArrayAsync());
        Assert.Equal("shared.pdf", served.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        // An altered link is refused: another signature, a later expiry, no signature, another operation.
        var sig = link[(link.IndexOf("&sig=", StringComparison.Ordinal) + 5)..];
        var forged = link.Replace(sig, new string('0', sig.Length), StringComparison.Ordinal);
        var expires = link.Split('?')[1].Split('&').Single(p => p.StartsWith("expires=", StringComparison.Ordinal));
        var extended = link.Replace(expires, $"expires={long.Parse(expires[8..], System.Globalization.CultureInfo.InvariantCulture) + 3600}", StringComparison.Ordinal);
        var unsigned = link[..link.IndexOf("&sig=", StringComparison.Ordinal)];
        var write = link.Replace("op=read", "op=write", StringComparison.Ordinal);
        foreach (var altered in new[] { forged, extended, unsigned, write })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(altered)).StatusCode);

        // Deleting the file stops the link, though the bytes are kept; restoring brings it back.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{uploaded.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(link)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/files/{uploaded.Id}/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(link)).StatusCode);
    }

    [Fact]
    public async Task Uploaded_files_are_counted_per_client_not_per_request()
    {
        if (!_infraReady) return;

        // Same storage root and key template as the other tests, with a limit of 3 files a minute.
        await using var factory = new GmWebApplicationFactory<Program>()
            .WithConfig("FileStorage:Local:RootPath", _root)
            .WithConfig("FileStorage:KeyTemplate", $"{_tenantId}/{{category}}/{{shard}}/{{id}}{{ext}}")
            .WithConfig("RateLimiting:Policies:uploaded-files:PermitLimit", "3");
        using var client = Client(factory);
        var batch = $"/api/v1/files/batch";
        (string, string, byte[]) Pdf(string name) => (name, "application/pdf", Encoding.UTF8.GetBytes("%PDF-1.7 " + name));

        // Two files: allowed.
        Assert.Equal(HttpStatusCode.OK, (await SendFilesAsync(client, batch, Pdf("a.pdf"), Pdf("b.pdf"))).StatusCode);

        // Two more would make four: the batch is refused whole, the file it got to store included.
        var refused = await SendFilesAsync(client, batch, Pdf("c.pdf"), Pdf("d.pdf"));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));
        Assert.Equal(["a.pdf", "b.pdf"], (await ListAsync(client, "")).Select(f => f.FileName).Order());
        Assert.Equal(2, StoredObjects().Length);

        // The third permit went to c.pdf before d.pdf was refused, so a single upload is out too.
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await SendFileAsync(client, HttpMethod.Post, $"/api/v1/files", Encoding.UTF8.GetBytes("%PDF-1.7 e"), "e.pdf", "application/pdf")).StatusCode);
    }

    [Fact]
    public async Task Retried_uploads_with_the_same_idempotency_key_are_stored_once()
    {
        if (!_infraReady) return;

        // A limit of 3 files a minute shows that a replayed retry doesn't count as another file.
        await using var factory = new GmWebApplicationFactory<Program>()
            .WithConfig("FileStorage:Local:RootPath", _root)
            .WithConfig("FileStorage:KeyTemplate", $"{_tenantId}/{{category}}/{{shard}}/{{id}}{{ext}}")
            .WithConfig("RateLimiting:Policies:uploaded-files:PermitLimit", "3");
        using var client = Client(factory);
        var single = $"/api/v1/files";
        async Task<HttpResponseMessage> WithKey(string key, Func<HttpClient, Task<HttpResponseMessage>> send)
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", key);
            try { return await send(client); }
            finally { client.DefaultRequestHeaders.Remove("Idempotency-Key"); }
        }
        Task<HttpResponseMessage> Pdf(HttpClient c, string name) =>
            SendFileAsync(c, HttpMethod.Post, single, Encoding.UTF8.GetBytes("%PDF-1.7 " + name), name, "application/pdf");

        // The retry gets the first answer back, marked as replayed; one file is stored. The key alone decides, so even
        // a different file under the same key gets that answer.
        var key = Guid.NewGuid().ToString();
        var first = await ExpectOkAsync(WithKey(key, c => Pdf(c, "a.pdf")));
        var retry = await WithKey(key, c => Pdf(c, "a.pdf"));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("true", Assert.Single(retry.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(first.Id, (await retry.Content.ReadFromJsonAsync<UploadResponse>())!.Id);
        Assert.Equal(first.Id, (await ExpectOkAsync(WithKey(key, c => Pdf(c, "other.pdf")))).Id);
        Assert.Single(await ListAsync(client, ""));
        Assert.Single(StoredObjects());

        // A refusal isn't remembered: the same key with a fixed file is a new upload.
        var fixedKey = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.BadRequest, (await WithKey(fixedKey, c =>
            SendFileAsync(c, HttpMethod.Post, single, Encoding.UTF8.GetBytes("<html></html>"), "b.html", "text/html"))).StatusCode);
        var second = await ExpectOkAsync(WithKey(fixedKey, c => Pdf(c, "b.pdf")));
        Assert.NotEqual(first.Id, second.Id);

        // Two files stored and one refused: three permits used, however many retries.
        Assert.Equal(2, (await ListAsync(client, "")).Count);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Pdf(client, "d.pdf")).StatusCode);
    }

    [Fact]
    public async Task Integrity_checks_record_missing_checksums_and_flag_damaged_files()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = Client(_factory);
        byte[] Pdf(string text) => Encoding.UTF8.GetBytes("%PDF-1.7 " + text);
        var altered = await UploadAsync(client, Pdf("altered"), "altered.pdf");
        var grown = await UploadAsync(client, Pdf("grown"), "grown.pdf");
        var lost = await UploadAsync(client, Pdf("lost"), "lost.pdf");

        // A file recorded without a checksum (as files were before checksums were kept).
        var legacyBytes = Pdf("legacy");
        var legacy = await UploadAsync(client, legacyBytes, "legacy.pdf");
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<StoredFile>()
                .Where(x => x.Id == legacy.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Checksum, (string?)null));
        }

        // Damage three files on disk: same size but different bytes, extra bytes, gone.
        var alteredPath = Path.Combine(_root, await StoragePathAsync(altered.Id));
        await File.WriteAllBytesAsync(alteredPath, Pdf("ALTERED"));
        await File.AppendAllTextAsync(Path.Combine(_root, await StoragePathAsync(grown.Id)), "more");
        File.Delete(Path.Combine(_root, await StoragePathAsync(lost.Id)));

        await RunIntegrityJobAsync();

        async Task<DetailsResponse> Details(Guid id) =>
            (await client.GetFromJsonAsync<DetailsResponse>($"/api/v1/files/{id}"))!;
        var legacyDetails = await Details(legacy.Id);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(legacyBytes)), legacyDetails.Checksum);
        Assert.Null(legacyDetails.IntegrityProblem);
        Assert.NotNull(legacyDetails.VerifiedAtUtc);
        Assert.Contains("checksum", (await Details(altered.Id)).IntegrityProblem);
        Assert.Contains("bytes, not the", (await Details(grown.Id)).IntegrityProblem);
        Assert.Contains("missing", (await Details(lost.Id)).IntegrityProblem);

        // The list finds them.
        Assert.Equal(new[] { altered.Id, grown.Id, lost.Id }.Order(),
            (await ListAsync(client, $"integrityProblem=true")).Select(f => f.Id).Order());
        Assert.Equal([legacy.Id], (await ListAsync(client, $"integrityProblem=false")).Select(f => f.Id));
    }

    [Fact]
    public async Task Retention_purges_files_deleted_too_long_ago_and_keeps_the_rest_restorable()
    {
        if (!_infraReady) return;

        using var client = Client(_factory);
        var old = await UploadAsync(client, Encoding.UTF8.GetBytes("%PDF-1.7 old"), "old.pdf");
        var recent = await UploadAsync(client, Encoding.UTF8.GetBytes("%PDF-1.7 recent"), "recent.pdf");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{old.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{recent.Id}")).StatusCode);

        // One deleted 40 days ago, the other just now; retention keeps deleted files for 30 days.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.NotNull(await context.Set<StoredFile>().Where(x => x.Id == recent.Id).Select(x => x.DeletedAtUtc).SingleAsync());
            await context.Set<StoredFile>().Where(x => x.Id == old.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAtUtc, DateTime.UtcNow.AddDays(-40)));
        }

        var job = new RetentionJob(_factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new RetentionOptions { DeletedFileDays = 30 }));
        var result = await job.RunAsync(f => f.StorageKey.StartsWith(_tenantId), default);

        // The old one is gone for good, bytes included; the recent one can still be restored.
        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{old.Id}?visibility=All")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/files/{old.Id}/restore", null)).StatusCode);
        Assert.Single(StoredObjects());
        Assert.Empty(await CleanupEntriesAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/files/{recent.Id}/restore", null)).StatusCode);
        using var after = _factory.Services.CreateScope();
        Assert.Null(await after.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Set<StoredFile>().Where(x => x.Id == recent.Id).Select(x => x.DeletedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task Uploads_stay_within_the_tenants_storage_quota()
    {
        if (!_infraReady) return;

        // Same storage root and key template as the other tests, with a 40-byte quota per tenant.
        await using var factory = new GmWebApplicationFactory<Program>()
            .WithConfig("FileStorage:Local:RootPath", _root)
            .WithConfig("FileStorage:KeyTemplate", $"{_tenantId}/{{category}}/{{shard}}/{{id}}{{ext}}")
            .WithConfig("Quotas:BytesPerTenant", "40");
        using var client = Client(factory);
        byte[] Pdf(int length) => [.. "%PDF-1.7 "u8, .. new byte[length - 9]];
        async Task<HttpResponseMessage> Upload(int length) =>
            await SendFileAsync(client, HttpMethod.Post, $"/api/v1/files", Pdf(length), "f.pdf", "application/pdf");
        async Task<UsageResponse> Usage() => (await client.GetFromJsonAsync<UsageResponse>($"/api/v1/files/usage"))!;

        var first = await Upload(20);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<UploadResponse>())!.Id;

        // 20 + 25 > 40: refused once its size is known, and its bytes are deleted.
        var over = await Upload(25);
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Contains("quota", await over.Content.ReadAsStringAsync());
        Assert.Single(StoredObjects());
        Assert.Empty(await CleanupEntriesAsync());
        Assert.Equal((20L, 40L, 1), (await Usage()) is var u ? (u.UsedBytes, u.QuotaBytes!.Value, u.FileCount) : default);

        // A deleted file still takes space until retention purges it.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/files/{firstId}")).StatusCode);
        Assert.Equal(20, (await Usage()).UsedBytes);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(25)).StatusCode);
    }

    [Fact]
    public async Task Large_uploads_are_streamed_into_storage_intact()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = Client(_factory);
        // Well past the 64 KB at which ASP.NET would spill a buffered form to disk.
        var bytes = new byte[3 * 1024 * 1024 + 17];
        new Random(42).NextBytes(bytes);
        "%PDF-1.7 "u8.CopyTo(bytes);

        var uploaded = await UploadAsync(client, bytes, "scan.pdf");

        Assert.Equal(bytes.Length, uploaded.Size);
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/v1/files/{uploaded.Id}/content"));
    }

    [Fact]
    public async Task An_oversized_upload_is_cut_off_without_reading_the_rest()
    {
        if (!_infraReady) return;

        const int limit = 64 * 1024;
        const int size = 8 * 1024 * 1024;
        // Same storage root and key template as the other tests, with a small size limit, and a count of the request
        // bytes the API has read by the time it starts answering.
        var bodyRead = new BodyReadCounter();
        await using var factory = new GmWebApplicationFactory<Program>()
            .WithConfig("FileStorage:Local:RootPath", _root)
            .WithConfig("FileStorage:KeyTemplate", $"{_tenantId}/{{category}}/{{shard}}/{{id}}{{ext}}")
            .WithConfig("FileStorage:MaxFileSize", limit.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithServices(services => services.AddSingleton<IStartupFilter>(bodyRead));
        using var client = Client(factory);

        var bytes = new byte[size];
        "%PDF-1.7 "u8.CopyTo(bytes);
        var response = await SendFileAsync(client, HttpMethod.Post, $"/api/v1/files", bytes, "huge.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"{limit}-byte limit", await response.Content.ReadAsStringAsync());
        // Refused soon after the limit: a server that buffered the form would have read all 8 MB first.
        Assert.InRange(bodyRead.AtResponse, limit, 1024 * 1024);
        Assert.Empty(await ListAsync(client, ""));
        Assert.Empty(StoredObjects());
        Assert.Empty(await CleanupEntriesAsync());
    }

    [Fact]
    public async Task Batch_upload_stores_each_file_and_reports_the_ones_it_refused()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = Client(_factory);
        var response = await SendFilesAsync(client, $"/api/v1/files/batch",
            ("a.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-1.7 a")),
            ("page.html", "text/html", Encoding.UTF8.GetBytes("<html></html>")),
            ("b.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", [(byte)'P', (byte)'K', 3, 4, 20, 0]));

        // One result per file, in the order sent: the refused file doesn't stop the one after it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<UploadResultResponse>>();
        Assert.NotNull(results);
        Assert.Equal(["a.pdf", "page.html", "b.docx"], results.Select(r => r.FileName));
        Assert.Equal([true, false, true], results.Select(r => r.Succeeded));
        Assert.Contains("text/html", results[1].Error);
        Assert.Null(results[1].File);

        // Each stored file is a file of its own, under the batch's tenant and user.
        var listed = await ListAsync(client, $"userId={_userId}");
        Assert.Equal(new[] { results[0].File!.Id, results[2].File!.Id }.Order(), listed.Select(f => f.Id).Order());
        Assert.Equal("%PDF-1.7 a", await client.GetStringAsync($"/api/v1/files/{results[0].File!.Id}/content"));
        Assert.Equal(2, StoredObjects().Length);
        Assert.Empty(await CleanupEntriesAsync());
    }

    [Fact]
    public async Task Upload_refuses_more_than_one_file_instead_of_dropping_them()
    {
        if (!_infraReady) return;

        using var client = Client(_factory);
        var response = await SendFilesAsync(client, $"/api/v1/files",
            ("a.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-1.7 a")),
            ("b.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-1.7 b")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("files/batch", await response.Content.ReadAsStringAsync());
        Assert.Empty(await ListAsync(client, ""));
        Assert.Empty(StoredObjects());
    }

    [Fact]
    public async Task Batch_upload_needs_between_one_and_twenty_files()
    {
        if (!_infraReady) return;

        using var client = Client(_factory);
        var url = $"/api/v1/files/batch";

        var tooMany = Enumerable.Range(0, 21)
            .Select(i => ($"f{i}.pdf", "application/pdf", Encoding.UTF8.GetBytes($"%PDF-1.7 {i}")))
            .ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await SendFilesAsync(client, url, tooMany)).StatusCode);

        using var empty = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url, empty)).StatusCode);

        Assert.Empty(await ListAsync(client, ""));
        Assert.Empty(StoredObjects());
    }

    [Fact]
    public async Task Upload_rejects_files_that_break_the_upload_rules()
    {
        if (!_infraReady) return; // integration test: no-op when Postgres isn't reachable

        using var client = Client(_factory);
        var url = $"/api/v1/files";

        // An executable dressed up as a PDF: the bytes give it away (FileStorage:VerifyContentType).
        var disguised = await SendFileAsync(client, HttpMethod.Post, url, [(byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0], "invoice.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.BadRequest, disguised.StatusCode);
        Assert.Contains("application/vnd.microsoft.portable-executable", await disguised.Content.ReadAsStringAsync());

        // A type outside FileStorage:AllowedContentTypes.
        var html = await SendFileAsync(client, HttpMethod.Post, url, Encoding.UTF8.GetBytes("<html></html>"), "page.html", "text/html");
        Assert.Equal(HttpStatusCode.BadRequest, html.StatusCode);

        Assert.Empty(await ListAsync(client, ""));
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

    private static Task<UploadResponse> UploadAsync(HttpClient client, byte[] bytes, string fileName) =>
        ExpectOkAsync(SendFileAsync(client, HttpMethod.Post, "/api/v1/files", bytes, fileName, "application/pdf"));

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

    // Several files in one multipart request: "files" parts for the batch endpoint, "file" parts for the single one.
    private static async Task<HttpResponseMessage> SendFilesAsync(
        HttpClient client, string url, params (string FileName, string ContentType, byte[] Bytes)[] files)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (fileName, contentType, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(part, url.Contains("/batch", StringComparison.Ordinal) ? "files" : "file", fileName);
        }
        return await client.PostAsync(url, form);
    }

    private static async Task<List<StoredFileResponse>> ListAsync(HttpClient client, string query)
    {
        var result = await client.GetFromJsonAsync<List<StoredFileResponse>>($"/api/v1/files?{query}");
        Assert.NotNull(result);
        return result;
    }

    private sealed record UploadResponse(Guid Id, string FileName, string ContentType, long Size, string? Checksum);

    // Counts the request bytes the app reads, and notes how many it had read when the response started.
    private sealed class BodyReadCounter : IStartupFilter
    {
        private long _read;

        public long AtResponse { get; private set; } = -1;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Request.Body = new CountingStream(context.Request.Body, this);
                context.Response.OnStarting(() =>
                {
                    AtResponse = Interlocked.Read(ref _read);
                    return Task.CompletedTask;
                });
                await nextMiddleware(context);
            });
            next(app);
        };

        private sealed class CountingStream(Stream inner, BodyReadCounter counter) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                Count(await inner.ReadAsync(buffer, cancellationToken));

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            private int Count(int read)
            {
                Interlocked.Add(ref counter._read, read);
                return read;
            }
        }
    }

    // Checks this test's files only: the database is shared with whatever else is in it.
    private async Task RunIntegrityJobAsync()
    {
        var job = new IntegrityCheckJob(_factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new IntegrityOptions { BatchSize = 100 }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IntegrityCheckJob>.Instance);
        await job.RunAsync(f => f.StorageKey.StartsWith(_tenantId), default);
    }

    private sealed record UsageResponse(string TenantId, long UsedBytes, long? QuotaBytes, int FileCount);

    private sealed record DetailsResponse(Guid Id, string? Checksum, DateTime? VerifiedAtUtc, string? IntegrityProblem);

    private sealed record PresignedResponse(Uri Url, DateTime ExpiresAtUtc);


    private sealed record UploadResultResponse(string FileName, bool Succeeded, UploadResponse? File, string? Error);

    private sealed record StoredFileResponse(
        Guid Id,
        string FileName,
        string ContentType,
        long Size,
        bool IsDeleted,
        Dictionary<string, string>? Metadata);
}
