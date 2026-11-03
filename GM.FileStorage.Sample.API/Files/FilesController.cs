using Asp.Versioning;
using GM.API.Controllers;
using GM.API.Models;
using GM.FileStorage.Sample.Application.Files.Commands.DeleteFile;
using GM.FileStorage.Sample.Application.Files.Commands.RestoreFile;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Files.Commands.UploadFile;
using GM.FileStorage.Sample.Application.Files.Commands.UploadFiles;
using GM.FileStorage.Sample.Application.Files.Queries.DownloadFile;
using GM.FileStorage.Sample.Application.Files.Queries.GetFileDetails;
using GM.FileStorage.Sample.Application.Files.Queries.GetFilePresignedUrl;
using GM.FileStorage.Sample.Application.Files.Queries.GetFilesList;
using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// Files Controller — upload (one file or a batch), list, inspect, download, delete, restore and presign files. A
/// file's content never changes after upload; new content is a new file. A file is
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
    /// Multipart <c>file</c>: exactly one (for several, use <c>POST files/batch</c>). Creates a new file identified by
    /// a GUID; its bytes are stored under a folder path from <c>FileStorage:KeyTemplate</c>, the stored name returned as
    /// <c>storageKey</c>. <c>tenantId</c> and <c>userId</c> are recorded as the file's searchable metadata.
    /// </remarks>
    /// <param name="file">The file to store</param>
    /// <param name="request">Tenant and user (query string)</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The stored file's id, storage key, size, checksum and content type</returns>
    [HttpPost("files", Name = nameof(UploadFile))]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadedFileModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadFile(
        IFormFile file,
        [FromQuery] UploadFileModel request,
        CancellationToken cancellationToken)
    {
        // Model binding would take the first file and quietly drop the rest; refuse instead, so nothing is lost.
        if (Request.Form.Files.Count > 1)
        {
            throw new BadRequestException(
                $"This endpoint stores one file, but {Request.Form.Files.Count} were sent. Use POST api/v1/files/batch for several.");
        }

        // ASP.NET has already buffered the multipart body (in memory, or on disk past 64 KB); from there the file is
        // streamed into storage.
        await using var stream = file.OpenReadStream();

        var command = request.Adapt<UploadFileCommand>();
        command.Content = stream;
        command.FileName = file.FileName;
        command.ContentType = ContentTypeOf(file);

        var response = await Mediator.Send(command, cancellationToken);
        var result = response.Adapt<UploadedFileModel>();
        return Ok(result);
    }

    /// <summary>
    /// Upload Files
    /// </summary>
    /// <remarks>
    /// Multipart <c>files</c>: 1 to 20 files, all for the same <c>tenantId</c> and <c>userId</c>. Each is stored as
    /// <c>POST files</c> would store it, with its own id. The response lists every file in the order sent, with the
    /// stored file or the reason it wasn't stored; one file failing doesn't stop the others. The whole request is
    /// still capped at the server's body limit (60 MB).
    /// </remarks>
    /// <param name="files">The files to store</param>
    /// <param name="request">Tenant and user (query string)</param>
    /// <param name="cancellationToken"></param>
    /// <returns>One result per file, in the order sent</returns>
    [HttpPost("files/batch", Name = nameof(UploadFiles))]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IEnumerable<UploadFileResultModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadFiles(
        List<IFormFile> files,
        [FromQuery] UploadFileModel request,
        CancellationToken cancellationToken)
    {
        var streams = new List<Stream>(files.Count);
        try
        {
            var command = new UploadFilesCommand { TenantId = request.TenantId, UserId = request.UserId };
            var items = new List<UploadFilesItem>(files.Count);
            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                items.Add(new UploadFilesItem { Content = stream, FileName = file.FileName, ContentType = ContentTypeOf(file) });
            }
            command.Files = items;

            var response = await Mediator.Send(command, cancellationToken);
            var result = response.Adapt<IEnumerable<UploadFileResultModel>>();
            return Ok(result);
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

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

    private static string ContentTypeOf(IFormFile file) =>
        string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
}
