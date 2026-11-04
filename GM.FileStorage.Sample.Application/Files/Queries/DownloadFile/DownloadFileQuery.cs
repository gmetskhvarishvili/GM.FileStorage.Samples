using FluentValidation;
using GM.EntityFramework.Domain.Common;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.DownloadFile;

/// <summary>
/// Opens a catalogued file's content for streaming. The catalog decides whether the file exists (a deleted row is
/// not downloadable); the bytes come straight from <see cref="IFileStorageService"/>, never buffered whole.
/// </summary>
public class DownloadFileQuery : IRequest<FileContentDto>
{
    public Guid Id { get; set; }

    /// <summary>Just this slice (an HTTP <c>Range</c> request); null for the whole file.</summary>
    public FileByteRange? Range { get; set; }

    /// <summary>The last N bytes (an HTTP suffix range, <c>bytes=-N</c>); used when <see cref="Range"/> is null.</summary>
    public long? SuffixLength { get; set; }
}

public class DownloadFileQueryValidator : AbstractValidator<DownloadFileQuery>
{
    public DownloadFileQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DownloadFileQueryHandler(IUnitOfWork unitOfWork, IFileStorageService storage, ICurrentActor actor)
    : IRequestHandler<DownloadFileQuery, FileContentDto>
{
    public async Task<FileContentDto> Handle(DownloadFileQuery request, CancellationToken cancellationToken)
    {
        var tenantId = actor.TenantOrShared();
        var entity = await unitOfWork.StoredFileRepository
            .FirstOrDefaultAsync(x => x.Id == request.Id
                                      && x.Metadata.Any(m => m.Name == StoredFileMetadataNames.TenantId && m.Value == tenantId)
                                      && x.IsActive
                                      && !x.IsDeleted
                                      && !x.IsHidden,
                true,
                null,
                cancellationToken);

        if (entity == null)
        {
            throw new NotFoundException(
                StringResource.StoredFile,
                StringResource.Id,
                request.Id);
        }

        FileDownloadResult download;
        try
        {
            // A suffix range needs the size, which the catalog already knows.
            var range = request.Range
                        ?? (request.SuffixLength is { } last ? new FileByteRange(Math.Max(0, entity.Size - last)) : null);
            download = range is { } r
                ? await storage.DownloadAsync(entity.StorageKey, r, cancellationToken)
                : await storage.DownloadAsync(entity.StorageKey, cancellationToken);
        }
        catch (FileStorageObjectNotFoundException)
        {
            // Catalogued but missing from storage (removed out of band): to the caller it's just not there.
            throw new NotFoundException(
                StringResource.File,
                StringResource.Id,
                request.Id);
        }

        // The caller owns the stream (the API hands it to a FileStreamResult, which disposes it).
        // A range past the end throws FileRangeNotSatisfiableException; the API answers it with 416.
        return new FileContentDto
        {
            Content = download.Content,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            Size = download.Metadata.Size,
            Range = download.Range,
        };
    }
}

public class FileContentDto
{
    public required Stream Content { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }

    /// <summary>The whole file's size, even when <see cref="Content"/> is a range.</summary>
    public long Size { get; init; }

    /// <summary>The slice <see cref="Content"/> holds, or null for the whole file.</summary>
    public FileByteRange? Range { get; init; }
}
