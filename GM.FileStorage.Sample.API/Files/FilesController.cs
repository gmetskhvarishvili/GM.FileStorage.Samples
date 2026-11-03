using Asp.Versioning;
using GM.API.Controllers;
using GM.API.Models;
using GM.FileStorage.Sample.Application.Files.Commands.DeleteFile;
using GM.FileStorage.Sample.Application.Files.Commands.RestoreFile;
using GM.FileStorage.Sample.Application.Files.Commands.UploadFile;
using GM.FileStorage.Sample.Application.Files.Queries.DownloadFile;
using GM.FileStorage.Sample.Application.Files.Queries.GetFileDetails;
using GM.FileStorage.Sample.Application.Files.Queries.GetFilePresignedUrl;
using GM.FileStorage.Sample.Application.Files.Queries.GetFilesList;
using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// Files Controller — upload, list, inspect, download, replace, delete, restore and presign files. A file is
/// identified by one GUID (its id); its bytes are stored under folders laid out by <c>FileStorage:KeyTemplate</c>,
/// and responses show the stored object's name as <c>storageKey</c> (e.g. <c>9f1c….pdf</c>). The bytes live in the storage provider
/// behind <c>IFileStorageService</c>; every upload is also recorded in the catalog (a stored-file row plus its
/// searchable metadata), which is what the list and details endpoints read.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
public class FilesController : BaseController
{
    /// <summary>
    /// Upload File
    /// </summary>
    /// <remarks>
    /// Multipart <c>file</c>. Creates a new file identified by a GUID; its bytes are stored under a folder path from
    /// <c>FileStorage:KeyTemplate</c>, the stored name returned as <c>storageKey</c>. <c>tenantId</c> and <c>userId</c> are
    /// recorded as the file's searchable metadata.
    /// </remarks>
    /// <param name="file">The file to store</param>
    /// <param name="request">Tenant and user (query string)</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The stored file's id, storage key, size, checksum and content type</returns>
    [HttpPost("files", Name = nameof(UploadFile))]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadedFileModel), StatusCodes.Status200OK)]
    public Task<IActionResult> UploadFile(
        IFormFile file,
        [FromQuery] UploadFileModel request,
        CancellationToken cancellationToken) =>
        UploadAsync(null, file, request, cancellationToken);

    /// <summary>
    /// Replace File Content
    /// </summary>
    /// <remarks>
    /// Multipart <c>file</c>: new content for an existing file (a deleted one is restored). The id stays the same;
    /// the bytes get a new <c>storageKey</c> and the previous version is removed.
    /// </remarks>
    /// <param name="id">The file to replace</param>
    /// <param name="file">The new content</param>
    /// <param name="request">Tenant and user (query string)</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The file's id, new storage key, size, checksum and content type</returns>
    [HttpPut("files/{id:guid}/content", Name = nameof(ReplaceFileContent))]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadedFileModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public Task<IActionResult> ReplaceFileContent(
        [FromRoute] Guid id,
        IFormFile file,
        [FromQuery] UploadFileModel request,
        CancellationToken cancellationToken) =>
        UploadAsync(id, file, request, cancellationToken);

    /// <summary>
    /// Get Files List
    /// </summary>
    /// <param name="request">Files list filters (tenant, user, storage folder, …) and paging</param>
    /// <param name="cancellationToken"></param>
    /// <returns>IEnumerable of stored files</returns>
    [HttpGet("files", Name = nameof(GetFilesList))]
    [ProducesResponseType(typeof(IEnumerable<StoredFileModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilesList(
        [FromQuery] GetFilesListModel request,
        CancellationToken cancellationToken)
    {
        var query = request.Adapt<GetFilesListQuery>();
        var response = await Mediator.Send(query, cancellationToken);
        var result = response.Items.Adapt<IEnumerable<StoredFileModel>>();
        AddPaginationHeader(response.TotalCount, response.PageSize, response.CurrentPage, response.TotalPages,
            response.LastKeyProperty, response.LastKeyValue, response.HasMore);
        return Ok(result);
    }

    /// <summary>
    /// Get File Details
    /// </summary>
    /// <param name="id">The file's id</param>
    /// <param name="request">Visibility scope (query string); defaults to visible-only. Widen it to inspect a deleted file.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Stored file details — storage key and metadata included</returns>
    [HttpGet("files/{id:guid}", Name = nameof(GetFileDetails))]
    [ProducesResponseType(typeof(StoredFileDetailsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileDetails(
        [FromRoute] Guid id,
        [FromQuery] GetBaseDetailsModel request,
        CancellationToken cancellationToken)
    {
        var response = await Mediator.Send(new GetFileDetailsQuery { Id = id, Visibility = request.Visibility }, cancellationToken);
        var result = response.Adapt<StoredFileDetailsModel>();
        return Ok(result);
    }

    /// <summary>
    /// Download File
    /// </summary>
    /// <remarks>
    /// Streamed straight from storage, never buffered whole. Honours a single HTTP <c>Range</c> (<c>bytes=0-1023</c>,
    /// <c>bytes=1024-</c>, <c>bytes=-500</c>) with <c>206 Partial Content</c>, so video players can seek and
    /// downloads can resume; only the requested bytes are read from storage.
    /// </remarks>
    /// <param name="id">The file's id</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("files/{id:guid}/content", Name = nameof(DownloadFile))]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status206PartialContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    public async Task<IActionResult> DownloadFile(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var query = new DownloadFileQuery { Id = id };
        var requested = Request.GetTypedHeaders().Range;
        // One range only; for several (rare), RFC 9110 lets us send the whole file instead.
        if (requested is { Unit.Value: "bytes", Ranges.Count: 1 } && requested.Ranges.Single() is var r)
        {
            if (r.From is { } from)
                query.Range = new FileByteRange(from, r.To is { } to ? to - from + 1 : null);
            else
                query.SuffixLength = r.To;
        }

        FileContentDto response;
        try
        {
            response = await Mediator.Send(query, cancellationToken);
        }
        catch (FileRangeNotSatisfiableException ex)
        {
            Response.Headers.ContentRange = $"bytes */{ex.Size}";
            return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
        }

        Response.Headers.AcceptRanges = "bytes";
        if (response.Range is not { } range)
            return File(response.Content, response.ContentType, response.FileName);

        await using (response.Content)
        {
            Response.StatusCode = StatusCodes.Status206PartialContent;
            Response.ContentType = response.ContentType;
            Response.ContentLength = range.Length;
            Response.Headers.ContentRange = range.ToContentRange(response.Size);
            await response.Content.CopyToAsync(Response.Body, cancellationToken);
        }

        return new EmptyResult();
    }

    /// <summary>
    /// Delete File
    /// </summary>
    /// <remarks>
    /// Marks the file deleted in the catalog; its bytes stay in storage, so <c>POST /files/{id}/restore</c> brings it
    /// back as it was.
    /// </remarks>
    /// <param name="id">The file's id</param>
    /// <param name="cancellationToken"></param>
    [HttpDelete("files/{id:guid}", Name = nameof(DeleteFile))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await Mediator.Send(new DeleteFileCommand { Id = id }, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Restore File
    /// </summary>
    /// <remarks>Brings a deleted file back with the content and metadata it had; nothing is re-uploaded.</remarks>
    /// <param name="id">The file's id</param>
    /// <param name="cancellationToken"></param>
    [HttpPost("files/{id:guid}/restore", Name = nameof(RestoreFile))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreFile(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await Mediator.Send(new RestoreFileCommand { Id = id }, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Presign File
    /// </summary>
    /// <remarks>A time-limited link to a file (needs <c>FileStorage:Local:PublicBaseUrl</c> for the local provider).</remarks>
    /// <param name="id">The file's id</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("files/{id:guid}/presign", Name = nameof(PresignFile))]
    [ProducesResponseType(typeof(PresignedUrlModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PresignFile(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var response = await Mediator.Send(new GetFilePresignedUrlQuery { Id = id }, cancellationToken);
        var result = response.Adapt<PresignedUrlModel>();
        return Ok(result);
    }

    // Upload and replace share this: null id uploads a new file, otherwise it replaces that file's content.
    private async Task<IActionResult> UploadAsync(
        Guid? id, IFormFile file, UploadFileModel request, CancellationToken cancellationToken)
    {
        // Streamed from the request straight into storage; never buffered whole.
        await using var stream = file.OpenReadStream();

        var command = request.Adapt<UploadFileCommand>();
        command.Id = id;
        command.Content = stream;
        command.FileName = file.FileName;
        command.ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        var response = await Mediator.Send(command, cancellationToken);
        var result = response.Adapt<UploadedFileModel>();
        return Ok(result);
    }
}
