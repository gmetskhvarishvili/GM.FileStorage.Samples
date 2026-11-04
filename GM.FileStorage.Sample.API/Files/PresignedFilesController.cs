using Asp.Versioning;
using GM.API.Controllers;
using GM.FileStorage.Local;
using GM.FileStorage.Sample.Application.Files.Queries.DownloadPresignedFile;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// Serves the presigned links the local disk provider hands out (<c>GET api/v1/files/{id}/presign</c>), with <c>FileStorage:Provider = Local</c>: <c>FileStorage:Local:PublicBaseUrl</c>
/// points here, through the gateway. S3 and Azure serve their own links, so with them every request here is refused.
/// </summary>
[ApiController]
[ApiVersionNeutral]
[Route("files")]
public class PresignedFilesController(LocalPresignedUrlValidator links) : BaseController
{
    /// <summary>
    /// Download Through A Presigned Link
    /// </summary>
    /// <remarks>
    /// The link as handed out: the file's storage key as the path, and <c>expires</c>, <c>op</c> and <c>sig</c>.
    /// A forged, altered or expired link gets a 403; a file deleted since the link was made, a 404.
    /// </remarks>
    /// <param name="storageKey">The stored object's key, folders included</param>
    /// <param name="expires">When the link expires (Unix seconds)</param>
    /// <param name="op">The link's operation; only <c>read</c> is served</param>
    /// <param name="sig">The link's signature (HMAC-SHA256, hex)</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{**storageKey}", Name = nameof(DownloadPresignedFile))]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadPresignedFile(
        [FromRoute] string storageKey,
        [FromQuery] string? expires,
        [FromQuery] string? op,
        [FromQuery] string? sig,
        CancellationToken cancellationToken)
    {
        if (!links.IsValid(storageKey, expires, op, sig))
            return StatusCode(StatusCodes.Status403Forbidden);

        var response = await Mediator.Send(new DownloadPresignedFileQuery { StorageKey = storageKey }, cancellationToken);
        return File(response.Content, response.ContentType, response.FileName);
    }
}
