using GM.FileStorage;
using GM.FileStorage.AzureBlob;
using GM.FileStorage.Local;
using GM.FileStorage.S3;
using GM.FileStorage.Sample.API;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// All three backends are registered; FileStorage:Provider (default "Local") decides which one
// IFileStorageService resolves to — swapping storage is a config change, not a code change. The S3
// and Azure clients are only constructed when their provider is selected, so the sample runs on
// disk out of the box with no cloud config.
builder.Services.AddGMFileStorage(builder.Configuration);
builder.Services.AddGMLocalFileStorage(builder.Configuration);
builder.Services.AddGMS3FileStorage(builder.Configuration);
builder.Services.AddGMAzureBlobFileStorage(builder.Configuration);

// KYC-style tenant/user-scoped keys (replaces the default date-partitioned strategy).
builder.Services.AddSingleton<IFileKeyStrategy, TenantKeyStrategy>();

builder.Services.AddHealthChecks();

var app = builder.Build();

// Liveness must not depend on downstream dependencies, so it runs no checks; readiness runs
// every registered health check (none here yet). See engineering baseline §11.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

// Upload a file (multipart). tenantId/userId shape the storage key via TenantKeyStrategy.
app.MapPost("/api/v1/files", async (IFormFile file, string? tenantId, string? userId, IFileStorageService storage) =>
{
    await using var stream = file.OpenReadStream();
    var meta = await storage.UploadAsync(new FileUploadRequest(stream, file.FileName, file.ContentType ?? "application/octet-stream")
    {
        Visibility = FileVisibility.Private,
        Metadata = new Dictionary<string, string>
        {
            ["tenantId"] = tenantId ?? "public",
            ["userId"] = userId ?? "anon",
        },
    });
    return Results.Ok(new { meta.Key, meta.Size, meta.Checksum, meta.ContentType });
}).DisableAntiforgery();

// Download — streams straight from storage, never buffering the whole file.
app.MapGet("/api/v1/files/{**key}", async (string key, IFileStorageService storage) =>
{
    if (!await storage.ExistsAsync(key))
        return Results.NotFound();

    var download = await storage.DownloadAsync(key);
    return Results.Stream(download.Content, download.Metadata.ContentType, download.Metadata.FileName);
});

app.MapDelete("/api/v1/files/{**key}", async (string key, IFileStorageService storage) =>
{
    await storage.DeleteAsync(key);
    return Results.NoContent();
});

// A time-limited link (needs FileStorage:Local:PublicBaseUrl for the local dev provider).
app.MapGet("/api/v1/presign/{**key}", async (string key, IFileStorageService storage) =>
{
    var url = await storage.GetPresignedUrlAsync(key, new PresignedUrlRequest { Expiry = TimeSpan.FromMinutes(10) });
    return Results.Ok(new { url });
});

await app.RunAsync();

// Exposed so the test project can boot the app with WebApplicationFactory.
public partial class Program
{
    // Only used as a WebApplicationFactory<Program> marker; never instantiated directly.
    protected Program() { }
}
