using FluentValidation;
using GM.Exceptions;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.DownloadFile;

/// <summary>
/// Opens a catalogued file's content for streaming. The catalog decides whether the file exists (a deleted row is
/// not downloadable); the bytes come straight from <see cref="IFileStorageService"/>, never buffered whole.
/// </summary>
public class DownloadFileQuery : IRequest<FileContentDto>
{
    public string? Key { get; set; }
}

public class DownloadFileQueryValidator : AbstractValidator<DownloadFileQuery>
{
    public DownloadFileQueryValidator()
    {
        RuleFor(x => x.Key).NotNull().NotEmpty();
    }
}

public class DownloadFileQueryHandler(IUnitOfWork unitOfWork, IFileStorageService storage)
    : IRequestHandler<DownloadFileQuery, FileContentDto>
{
    public async Task<FileContentDto> Handle(DownloadFileQuery request, CancellationToken cancellationToken)
    {
        var entity = await unitOfWork.StoredFileRepository
            .FirstOrDefaultAsync(x => x.Key == request.Key
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
                StringResource.Key,
                request.Key!);
        }

        FileDownloadResult download;
        try
        {
            download = await storage.DownloadAsync(entity.Key, cancellationToken);
        }
        catch (FileStorageObjectNotFoundException)
        {
            // Catalogued but missing from storage (removed out of band): to the caller it's just not there.
            throw new NotFoundException(
                StringResource.File,
                StringResource.Key,
                request.Key!);
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
