using Asp.Versioning;
using GM.API.Controllers;
using GM.API.Models;
using GM.FileStorage.Sample.Application.Files.Commands.DeleteFile;
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
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// Files Controller — upload, list, inspect, download, delete and presign files. The bytes live in the storage
/// provider behind <c>IFileStorageService</c>; every upload is also recorded in the catalog (a stored-file row plus
/// its searchable metadata), which is what the list and details endpoints read.
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
    /// Multipart <c>file</c>. <c>tenantId</c> and <c>userId</c> shape the storage key via TenantKeyStrategy
    /// (<c>tenants/{tenantId}/users/{userId}/{guid}{ext}</c>) and are recorded as the file's metadata. Send
    /// <c>key</c> to upload to an explicit key instead — uploading to an existing (or deleted) key replaces it.
    /// </remarks>
    /// <param name="file">The file to store</param>
    /// <param name="request">Tenant, user and optional explicit key (query string)</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The stored file's id, key, size, checksum and content type</returns>
    [HttpPost("files", Name = nameof(UploadFile))]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadedFileModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadFile(
        IFormFile file,
        [FromQuery] UploadFileModel request,
        CancellationToken cancellationToken)
    {
        // Streamed from the request straight into storage; never buffered whole.
        await using var stream = file.OpenReadStream();

        var command = request.Adapt<UploadFileCommand>();
        command.Content = stream;
        command.FileName = file.FileName;
        command.ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        var response = await Mediator.Send(command, cancellationToken);
        var result = response.Adapt<UploadedFileModel>();
        return Ok(result);
    }

    /// <summary>
    /// Get Files List
    /// </summary>
    /// <param name="request">Files list filters (tenant, user, key prefix, …) and paging</param>
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
    /// <param name="id">Stored file Id to get</param>
    /// <param name="request">Visibility scope (query string); defaults to visible-only. Widen it to inspect a deleted file.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Stored file details, metadata included</returns>
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
    /// <remarks>Streamed straight from storage, never buffered whole.</remarks>
    /// <param name="key">The file's storage key</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("files/{**key}", Name = nameof(DownloadFile))]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(
        [FromRoute] string key,
        CancellationToken cancellationToken)
    {
        var response = await Mediator.Send(new DownloadFileQuery { Key = key }, cancellationToken);
        return File(response.Content, response.ContentType, response.FileName);
    }

    /// <summary>
    /// Delete File
    /// </summary>
    /// <remarks>Removes the bytes from storage and soft-deletes the catalog row (kept as an audit trail).</remarks>
    /// <param name="key">The file's storage key</param>
    /// <param name="cancellationToken"></param>
    [HttpDelete("files/{**key}", Name = nameof(DeleteFile))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile(
        [FromRoute] string key,
        CancellationToken cancellationToken)
    {
        await Mediator.Send(new DeleteFileCommand { Key = key }, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Presign File
    /// </summary>
    /// <remarks>A time-limited link to a file (needs <c>FileStorage:Local:PublicBaseUrl</c> for the local provider).</remarks>
    /// <param name="key">The file's storage key</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("presign/{**key}", Name = nameof(PresignFile))]
    [ProducesResponseType(typeof(PresignedUrlModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PresignFile(
        [FromRoute] string key,
        CancellationToken cancellationToken)
    {
        var response = await Mediator.Send(new GetFilePresignedUrlQuery { Key = key }, cancellationToken);
        var result = response.Adapt<PresignedUrlModel>();
        return Ok(result);
    }
}
