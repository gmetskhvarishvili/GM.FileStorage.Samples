using FluentValidation;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Files.Queries.DownloadFile;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.DownloadPresignedFile;

/// <summary>
/// The file a presigned link on local disk points at, by its storage key (the link's path). The caller has already
/// checked the link's signature; this only serves a file that is still live, so a link to a file deleted since it was
/// handed out stops working, even though the bytes are kept for a restore.
/// </summary>
public class DownloadPresignedFileQuery : IRequest<FileContentDto>
{
    public string? StorageKey { get; set; }
}

public class DownloadPresignedFileQueryValidator : AbstractValidator<DownloadPresignedFileQuery>
{
    public DownloadPresignedFileQueryValidator()
    {
        RuleFor(x => x.StorageKey).NotEmpty().MaximumLength(StoredFile.StorageKeyMaxLength);
    }
}

public class DownloadPresignedFileQueryHandler(IUnitOfWork unitOfWork, IFileStorageService storage)
    : IRequestHandler<DownloadPresignedFileQuery, FileContentDto>
{
    public async Task<FileContentDto> Handle(DownloadPresignedFileQuery request, CancellationToken cancellationToken)
    {
        var entity = await unitOfWork.StoredFileRepository
            .FirstOrDefaultAsync(x => x.StorageKey == request.StorageKey
                                      && x.IsActive
                                      && !x.IsDeleted
                                      && !x.IsHidden,
                true,
                null,
                cancellationToken)
            ?? throw new NotFoundException(StringResource.StoredFile, StringResource.StorageKey, request.StorageKey!);

        FileDownloadResult download;
        try
        {
            download = await storage.DownloadAsync(entity.StorageKey, cancellationToken);
        }
        catch (FileStorageObjectNotFoundException)
        {
            throw new NotFoundException(StringResource.File, StringResource.StorageKey, request.StorageKey!);
        }

        return new FileContentDto
        {
            Content = download.Content,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            Size = entity.Size,
        };
    }
}
