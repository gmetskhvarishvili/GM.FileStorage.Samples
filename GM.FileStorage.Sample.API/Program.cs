using GM.FileStorage;
using GM.FileStorage.Local;
using GM.FileStorage.Sample.API;

var builder = WebApplication.CreateBuilder(args);

// FileStorage:Provider picks the backend. Here it's "Local" (disk); reference GM.FileStorage.S3 /
// .AzureBlob and add their AddGM… calls to switch by config alone.
builder.Services.AddGMFileStorage(builder.Configuration);
builder.Services.AddGMLocalFileStorage(builder.Configuration);

// KYC-style tenant/user-scoped keys (replaces the default date-partitioned strategy).
builder.Services.AddSingleton<IFileKeyStrategy, TenantKeyStrategy>();

var app = builder.Build();

// Upload a file (multipart). tenantId/userId shape the storage key via TenantKeyStrategy.
app.MapPost("/files", async (IFormFile file, string? tenantId, string? userId, IFileStorageService storage) =>
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
app.MapGet("/files/{**key}", async (string key, IFileStorageService storage) =>
{
    if (!await storage.ExistsAsync(key))
        return Results.NotFound();

    var download = await storage.DownloadAsync(key);
    return Results.Stream(download.Content, download.Metadata.ContentType, download.Metadata.FileName);
});

app.MapDelete("/files/{**key}", async (string key, IFileStorageService storage) =>
{
    await storage.DeleteAsync(key);
    return Results.NoContent();
});

// A time-limited link (needs FileStorage:Local:PublicBaseUrl for the local dev provider).
app.MapGet("/presign/{**key}", async (string key, IFileStorageService storage) =>
{
    var url = await storage.GetPresignedUrlAsync(key, new PresignedUrlRequest { Expiry = TimeSpan.FromMinutes(10) });
    return Results.Ok(new { url });
});

app.Run();

// Exposed so the test project can boot the app with WebApplicationFactory.
public partial class Program;
