using FluentValidation;
using GM.EntityFramework.Domain.Common;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.DownloadFile;

/// <summary>
/// Opens a catalogued file's content for streaming. The catalog decides whether the file exists (a deleted row is
/// not downloadable); the bytes come straight from <see cref="IFileStorageService"/>, never buffered whole.
/// Always the whole file. Each download is recorded in the file's history, with who opened it.
/// </summary>
public class DownloadFileQuery : IRequest<FileContentDto>
{
    public Guid Id { get; set; }
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
                                      && x.TenantId == tenantId
                                      && x.IsActive
                                      && !x.IsDeleted
                                      && !x.IsHidden,
                false,
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
            download = await storage.DownloadAsync(entity.StorageKey, cancellationToken);
        }
        catch (FileStorageObjectNotFoundException)
        {
            // Catalogued but missing from storage (removed out of band): to the caller it's just not there.
            throw new NotFoundException(
                StringResource.File,
                StringResource.Id,
                request.Id);
        }

        entity.RecordDownload();
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await download.DisposeAsync();
            throw;
        }

        // The caller owns the stream (the API hands it to a FileStreamResult, which disposes it).
        return new FileContentDto
        {
            Content = download.Content,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
        };
    }
}

public class FileContentDto
{
    public required Stream Content { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
}
