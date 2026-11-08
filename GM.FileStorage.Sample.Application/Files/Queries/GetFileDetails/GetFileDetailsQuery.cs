using FluentValidation;
using GM.API.Application.Models;
using GM.EntityFramework.Domain.Specifications;
using GM.EntityFramework.Domain.Common;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
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

public class GetFileDetailsQueryHandler(IUnitOfWork unitOfWork, ICurrentActor actor)
    : IRequestHandler<GetFileDetailsQuery, StoredFileDetailsDto>
{
    public async Task<StoredFileDetailsDto> Handle(GetFileDetailsQuery request, CancellationToken cancellationToken)
    {
        var entity = await unitOfWork.StoredFileRepository
            .Query(true, null)
            .OwnedBy(actor.TenantOrShared())
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
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Checksum { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string? TenantId { get; set; }

    /// <summary>Who uploaded the file: their user id (<c>X-User-Id</c>), kept in the audit column; null when none was sent.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>When the stored bytes were last checked against this record (UTC).</summary>
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>What the last check found wrong with the stored bytes; null when they matched.</summary>
    public string? IntegrityProblem { get; set; }
}
