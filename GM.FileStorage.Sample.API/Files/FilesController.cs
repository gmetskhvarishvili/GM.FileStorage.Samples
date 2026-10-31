using Asp.Versioning;
using GM.API.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// Upload, download, delete and presign files through the provider-agnostic <see cref="IFileStorageService"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
public class FilesController(IFileStorageService storage) : BaseController
{
    /// <summary>
    /// Upload a file (multipart <c>file</c>). <paramref name="tenantId"/> and <paramref name="userId"/> shape the
    /// storage key via <see cref="TenantKeyStrategy"/>: <c>tenants/{tenantId}/users/{userId}/{guid}{ext}</c>.
    /// </summary>
    /// <param name="file">The file to store.</param>
    /// <param name="tenantId">Tenant the file belongs to (default <c>public</c>).</param>
    /// <param name="userId">User the file belongs to (default <c>anon</c>).</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The stored file's key, size, checksum and content type.</returns>
    [HttpPost("files", Name = nameof(UploadFile))]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadedFileModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadFile(
        IFormFile file,
        [FromQuery] string? tenantId,
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        // Streamed from the request straight into storage; never buffered whole.
        await using var stream = file.OpenReadStream();
        var meta = await storage.UploadAsync(new FileUploadRequest(stream, file.FileName, file.ContentType ?? "application/octet-stream")
        {
            Visibility = FileVisibility.Private,
            Metadata = new Dictionary<string, string>
            {
                ["tenantId"] = tenantId ?? "public",
                ["userId"] = userId ?? "anon",
            },
        }, cancellationToken);

        return Ok(new UploadedFileModel(meta.Key, meta.Size, meta.Checksum, meta.ContentType));
    }

    /// <summary>Download a file — streamed straight from storage, never buffered whole.</summary>
    /// <param name="key">The file's storage key.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("files/{**key}", Name = nameof(DownloadFile))]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(string key, CancellationToken cancellationToken)
    {
        if (!await storage.ExistsAsync(key, cancellationToken))
            return NotFound();

        var download = await storage.DownloadAsync(key, cancellationToken);
        return File(download.Content, download.Metadata.ContentType, download.Metadata.FileName);
    }

    /// <summary>Delete a file.</summary>
    /// <param name="key">The file's storage key.</param>
    /// <param name="cancellationToken"></param>
    [HttpDelete("files/{**key}", Name = nameof(DeleteFile))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteFile(string key, CancellationToken cancellationToken)
    {
        await storage.DeleteAsync(key, cancellationToken);
        return NoContent();
    }

    /// <summary>A time-limited link to a file (needs <c>FileStorage:Local:PublicBaseUrl</c> for the local provider).</summary>
    /// <param name="key">The file's storage key.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("presign/{**key}", Name = nameof(PresignFile))]
    [ProducesResponseType(typeof(PresignedUrlModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> PresignFile(string key, CancellationToken cancellationToken)
    {
        var url = await storage.GetPresignedUrlAsync(key, new PresignedUrlRequest { Expiry = TimeSpan.FromMinutes(10) }, cancellationToken);
        return Ok(new PresignedUrlModel(url));
    }
}

/// <summary>A stored file.</summary>
/// <param name="Key">Storage key.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Checksum">Content checksum, when the provider computes one.</param>
/// <param name="ContentType">MIME type.</param>
public sealed record UploadedFileModel(string Key, long Size, string? Checksum, string ContentType);

/// <summary>A time-limited link.</summary>
/// <param name="Url">The link.</param>
public sealed record PresignedUrlModel(Uri Url);
