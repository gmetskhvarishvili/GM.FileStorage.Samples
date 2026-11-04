using FluentValidation;
using GM.EntityFramework.Domain.Common;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.GetFilePresignedUrl;

/// <summary>A time-limited link to a catalogued file (the local provider needs <c>FileStorage:Local:PublicBaseUrl</c>).</summary>
public class GetFilePresignedUrlQuery : IRequest<FilePresignedUrlDto>
{
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(10);

    public Guid Id { get; set; }
}

public class GetFilePresignedUrlQueryValidator : AbstractValidator<GetFilePresignedUrlQuery>
{
    public GetFilePresignedUrlQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class GetFilePresignedUrlQueryHandler(IUnitOfWork unitOfWork, IFileStorageService storage, ICurrentActor actor)
    : IRequestHandler<GetFilePresignedUrlQuery, FilePresignedUrlDto>
{
    public async Task<FilePresignedUrlDto> Handle(GetFilePresignedUrlQuery request, CancellationToken cancellationToken)
    {
        var tenantId = actor.RequireTenant();
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

        // The link points at the current version's bytes (the file's storage key), not the logical key.
        // Some setups can't hand out direct links: encrypted storage (a link would serve ciphertext) or the local
        // provider without a PublicBaseUrl. That's a 400 with the reason; download through the API instead.
        var expiry = GetFilePresignedUrlQuery.DefaultExpiry;
        Uri url;
        try
        {
            url = await storage.GetPresignedUrlAsync(
                entity.StorageKey, new PresignedUrlRequest { Expiry = expiry }, cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        return new FilePresignedUrlDto { Url = url, ExpiresAtUtc = DateTime.UtcNow.Add(expiry) };
    }
}

public class FilePresignedUrlDto
{
    public required Uri Url { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
}
