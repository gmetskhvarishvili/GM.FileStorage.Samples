using FluentValidation;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Files.Commands.UploadFile;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.Mediator.Contracts;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
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

    public IReadOnlyList<UploadFilesItem> Files { get; set; } = [];

    /// <summary>Tenant every file belongs to; defaults to <c>public</c>.</summary>
    public string? TenantId { get; set; }

    /// <summary>User every file belongs to; defaults to <c>anon</c>.</summary>
    public string? UserId { get; set; }
}

/// <summary>One file of a batch.</summary>
public class UploadFilesItem
{
    /// <summary>The bytes, streamed straight into storage (never buffered whole).</summary>
    public Stream? Content { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
}

public class UploadFilesCommandValidator : AbstractValidator<UploadFilesCommand>
{
    public UploadFilesCommandValidator()
    {
        RuleFor(x => x.Files).NotEmpty()
            .Must(files => files.Count <= UploadFilesCommand.MaxFiles)
            .WithMessage($"At most {UploadFilesCommand.MaxFiles} files can be uploaded at once.");
        RuleFor(x => x.TenantId).MaximumLength(StoredFileMetadata.ValueMaxLength);
        RuleFor(x => x.UserId).MaximumLength(StoredFileMetadata.ValueMaxLength);
    }
}

/// <remarks>
/// Files are stored one after another, in the order sent. A file the caller got wrong (a validation failure or a
/// broken upload rule) is reported with its reason; anything else is logged and reported as a failure to retry. Either
/// way the files before and after it are unaffected, so a retry only needs the failed ones.
/// </remarks>
public class UploadFilesCommandHandler(IMediator mediator, ILogger<UploadFilesCommandHandler> logger)
    : IRequestHandler<UploadFilesCommand, IReadOnlyList<UploadFileResultDto>>
{
    public async Task<IReadOnlyList<UploadFileResultDto>> Handle(UploadFilesCommand request, CancellationToken cancellationToken)
    {
        var results = new List<UploadFileResultDto>(request.Files.Count);
        foreach (var file in request.Files)
        {
            var command = new UploadFileCommand
            {
                Content = file.Content,
                FileName = file.FileName,
                ContentType = file.ContentType,
                TenantId = request.TenantId,
                UserId = request.UserId,
            };

            try
            {
                var stored = await mediator.Send(command, cancellationToken);
                results.Add(new UploadFileResultDto { FileName = file.FileName, Succeeded = true, File = stored });
            }
            catch (CustomException ex)
            {
                results.Add(new UploadFileResultDto { FileName = file.FileName, Error = ex.Message });
            }
#pragma warning disable CA1031 // Reported per file, so the rest of the batch still gets stored.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Storing {FileName} from a batch upload failed.", file.FileName);
                results.Add(new UploadFileResultDto
                {
                    FileName = file.FileName,
                    Error = "The file couldn't be stored. Try uploading it again.",
                });
            }
        }

        return results;
    }
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
