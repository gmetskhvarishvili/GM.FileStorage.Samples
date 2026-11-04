using Asp.Versioning;
using GM.API.Controllers;
using GM.FileStorage.Local;
using GM.FileStorage.Sample.API.Files.Multipart;
using GM.FileStorage.Sample.Application.Files.Commands.ReceiveDirectUpload;
using GM.FileStorage.Sample.Application.Files.Queries.DownloadPresignedFile;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// Serves the presigned links the local disk provider hands out, read links (<c>GET api/v1/files/{id}/presign</c>)
/// and write links (direct uploads), with <c>FileStorage:Provider = Local</c>: <c>FileStorage:Local:PublicBaseUrl</c>
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

    /// <summary>
    /// Upload Through A Presigned Link
    /// </summary>
    /// <remarks>
    /// The write end of a direct upload on local disk (<c>POST api/v1/files/direct-uploads</c>): the request body is
    /// the file, streamed into storage. Then complete the upload. Only a pending upload's key can be written; a forged,
    /// altered or expired link gets a 403. The usual upload rules apply.
    /// </remarks>
    /// <param name="storageKey">The stored object's key, folders included</param>
    /// <param name="expires">When the link expires (Unix seconds)</param>
    /// <param name="op">The link's operation; must be <c>write</c></param>
    /// <param name="sig">The link's signature (HMAC-SHA256, hex)</param>
    /// <param name="cancellationToken"></param>
    [HttpPut("{**storageKey}", Name = nameof(UploadThroughPresignedLink))]
    // The body is the file itself (application/pdf, image/png, …), not JSON.
    [AcceptsAnyContentType]
    [RequestBodyErrors]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadThroughPresignedLink(
        [FromRoute] string storageKey,
        [FromQuery] string? expires,
        [FromQuery] string? op,
        [FromQuery] string? sig,
        CancellationToken cancellationToken)
    {
        if (!links.IsValid(storageKey, expires, op, sig, PresignedUrlOperation.Write))
            return StatusCode(StatusCodes.Status403Forbidden);

        await Mediator.Send(new ReceiveDirectUploadCommand { StorageKey = storageKey, Content = Request.Body }, cancellationToken);
        return Ok();
    }
}
