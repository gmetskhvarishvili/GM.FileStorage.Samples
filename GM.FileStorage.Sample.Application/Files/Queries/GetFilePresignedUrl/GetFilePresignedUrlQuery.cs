using FluentValidation;
using GM.Exceptions;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.GetFilePresignedUrl;

/// <summary>A time-limited link to a catalogued file (the local provider needs <c>FileStorage:Local:PublicBaseUrl</c>).</summary>
public class GetFilePresignedUrlQuery : IRequest<FilePresignedUrlDto>
{
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(10);

    public string? Key { get; set; }
}

public class GetFilePresignedUrlQueryValidator : AbstractValidator<GetFilePresignedUrlQuery>
{
    public GetFilePresignedUrlQueryValidator()
    {
        RuleFor(x => x.Key).NotNull().NotEmpty();
    }
}

public class GetFilePresignedUrlQueryHandler(IUnitOfWork unitOfWork, IFileStorageService storage)
    : IRequestHandler<GetFilePresignedUrlQuery, FilePresignedUrlDto>
{
    public async Task<FilePresignedUrlDto> Handle(GetFilePresignedUrlQuery request, CancellationToken cancellationToken)
    {
        var exists = await unitOfWork.StoredFileRepository
            .ExistsAsync(x => x.Key == request.Key
                              && x.IsActive
                              && !x.IsDeleted
                              && !x.IsHidden,
                cancellationToken);

        if (!exists)
        {
            throw new NotFoundException(
                StringResource.StoredFile,
                StringResource.Key,
                request.Key!);
        }

        var expiry = GetFilePresignedUrlQuery.DefaultExpiry;
        var url = await storage.GetPresignedUrlAsync(
            request.Key!, new PresignedUrlRequest { Expiry = expiry }, cancellationToken);

        return new FilePresignedUrlDto { Url = url, ExpiresAtUtc = DateTime.UtcNow.Add(expiry) };
    }
}

public class FilePresignedUrlDto
{
    public required Uri Url { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
}
