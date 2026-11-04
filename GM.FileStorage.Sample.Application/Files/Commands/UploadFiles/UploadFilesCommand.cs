using FluentValidation;
using GM.EntityFramework.Domain.Common;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Application.Files.Commands.DiscardFiles;
using GM.FileStorage.Sample.Application.Files.Commands.UploadFile;
using GM.Mediator.Contracts;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.UploadFiles;

/// <summary>
/// Stores several new files in one request, each exactly as <see cref="UploadFileCommand"/> would (its own id, its
/// own upload rules, its own transaction), and reports the outcome of each. One file failing doesn't stop the rest.
/// </summary>
public class UploadFilesCommand : IRequest<IReadOnlyList<UploadFileResultDto>>
{
    /// <summary>Most files one batch may hold.</summary>
    public const int MaxFiles = 20;

    /// <summary>
    /// The files, in the order sent. Read one at a time as they arrive (straight off the request body), so their
    /// number is only known at the end.
    /// </summary>
    public IAsyncEnumerable<UploadFilesItem> Files { get; set; } = AsyncEnumerable.Empty<UploadFilesItem>();
}

/// <summary>One file of a batch.</summary>
public class UploadFilesItem
{
    /// <summary>The bytes, streamed straight into storage (never buffered whole).</summary>
    public Stream? Content { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
}

public class UploadFilesCommandValidator : AbstractValidator<UploadFilesCommand>;

/// <remarks>
/// Files are stored one after another, as they arrive. A file the caller got wrong (a validation failure or a broken
/// upload rule) is reported with its reason; anything else is logged and reported as a failure to retry. Either way
/// the files before and after it are unaffected, so a retry only needs the failed ones.
/// <para>
/// The batch as a whole must hold 1 to <see cref="UploadFilesCommand.MaxFiles"/> files. That's only known as the
/// files arrive, so a file past the limit refuses the whole batch with a 400. Whenever the request fails as a whole
/// like that (or its body turns out too large, or the client goes away), the files already stored are discarded
/// (<see cref="DiscardFilesCommand"/>): the caller never got their ids, so nothing is kept.
/// </para>
/// </remarks>
public class UploadFilesCommandHandler(IMediator mediator, ICurrentActor actor, ILogger<UploadFilesCommandHandler> logger)
    : IRequestHandler<UploadFilesCommand, IReadOnlyList<UploadFileResultDto>>
{
    public async Task<IReadOnlyList<UploadFileResultDto>> Handle(UploadFilesCommand request, CancellationToken cancellationToken)
    {
        // Without a tenant no file could be stored: refuse the request rather than fail every file.
        actor.RequireTenant();

        var results = new List<UploadFileResultDto>();
        try
        {
            await foreach (var file in request.Files.WithCancellation(cancellationToken))
            {
                if (results.Count == UploadFilesCommand.MaxFiles)
                {
                    throw new BadRequestException(
                        $"At most {UploadFilesCommand.MaxFiles} files can be uploaded at once. Nothing was kept.");
                }

                results.Add(await UploadAsync(file, cancellationToken));
            }
        }
        catch
        {
            // The request as a whole failed (too many files, a body too large or cut off, the client gone): the
            // caller never sees these results, so nothing from it is kept.
            await DiscardAsync(results);
            throw;
        }

        if (results.Count == 0)
            throw new BadRequestException("No files were sent.");

        return results;
    }

    private async Task<UploadFileResultDto> UploadAsync(
        UploadFilesItem file, CancellationToken cancellationToken)
    {
        var command = new UploadFileCommand
        {
            Content = file.Content,
            FileName = file.FileName,
            ContentType = file.ContentType,
        };

        try
        {
            var stored = await mediator.Send(command, cancellationToken);
            return new UploadFileResultDto { FileName = file.FileName, Succeeded = true, File = stored };
        }
        catch (CustomException ex)
        {
            return new UploadFileResultDto { FileName = file.FileName, Error = ex.Message };
        }
#pragma warning disable CA1031 // Reported per file, so the rest of the batch still gets stored.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Storing {FileName} from a batch upload failed.", file.FileName);
            return new UploadFileResultDto
            {
                FileName = file.FileName,
                Error = "The file couldn't be stored. Try uploading it again.",
            };
        }
    }

    // The caller never sees these files, so they go entirely (not soft-deleted).
    private Task DiscardAsync(List<UploadFileResultDto> results) =>
        mediator.Send(
            new DiscardFilesCommand { Ids = results.Where(r => r.File != null).Select(r => r.File!.Id).ToList() },
            CancellationToken.None);
}

/// <summary>What happened to one file of a batch: the stored file, or why it wasn't stored.</summary>
public class UploadFileResultDto
{
    public string? FileName { get; set; }
    public bool Succeeded { get; set; }

    /// <summary>The stored file, when <see cref="Succeeded"/>.</summary>
    public UploadedFileDto? File { get; set; }

    /// <summary>Why the file wasn't stored, otherwise.</summary>
    public string? Error { get; set; }
}
