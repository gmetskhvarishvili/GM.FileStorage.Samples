using FluentValidation;
using GM.API.Application.Models;
using GM.EntityFramework.Domain.Specifications;
using GM.Exceptions;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using Mapster;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.GetFileDetails;

public class GetFileDetailsQuery : GetBaseDetailsQuery, IRequest<StoredFileDetailsDto>
{
}

public class GetFileDetailsQueryValidator : AbstractValidator<GetFileDetailsQuery>
{
    public GetFileDetailsQueryValidator()
    {
        RuleFor(x => x.Id).NotNull().NotEmpty();
    }
}

public class GetFileDetailsQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetFileDetailsQuery, StoredFileDetailsDto>
{
    public async Task<StoredFileDetailsDto> Handle(GetFileDetailsQuery request, CancellationToken cancellationToken)
    {
        var entity = await unitOfWork.StoredFileRepository
            .Query(true, null)
            .Include(x => x.Metadata)
            .FirstOrDefaultAsync(x => x.Id == request.Id
                                      && (request.Visibility.HasFlag(VisibilityScope.IncludeInactive) || x.IsActive)
                                      && (request.Visibility.HasFlag(VisibilityScope.IncludeDeleted) || !x.IsDeleted)
                                      && (request.Visibility.HasFlag(VisibilityScope.IncludeHidden) || !x.IsHidden),
                cancellationToken);

        if (entity == null)
        {
            throw new NotFoundException(
                StringResource.StoredFile,
                StringResource.Id,
                request.Id!);
        }

        return entity.Adapt<StoredFileDetailsDto>();
    }
}

public class StoredFileDetailsDto : AuditableDto
{
    public Guid Id { get; set; }
    /// <summary>The stored object's name, e.g. <c>9f1c….pdf</c>.</summary>
    public string? StorageKey { get; set; }

    /// <summary>The folder it's stored in, e.g. <c>documents/9f/</c>.</summary>
    public string? Folder { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Checksum { get; set; }
    public bool IsPublic { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}
