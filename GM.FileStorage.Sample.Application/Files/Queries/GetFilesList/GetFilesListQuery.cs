using FluentValidation;
using GM.API.Application.Models;
using GM.EntityFramework.Domain.Specifications;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Specifications;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using Mapster;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.GetFilesList;

public class GetFilesListQuery : GetBaseListQuery, IRequest<PagedListDto<StoredFileDto>>
{
    public Guid? Id { get; set; }

    /// <summary>Only files uploaded for this tenant (the <c>tenantId</c> metadata row).</summary>
    public string? TenantId { get; set; }

    /// <summary>Only files uploaded for this user (the <c>userId</c> metadata row).</summary>
    public string? UserId { get; set; }

    /// <summary>Only files whose storage key starts with this, e.g. <c>tenants/acme/</c>.</summary>
    public string? KeyPrefix { get; set; }

    public string? ContentType { get; set; }
}

public class GetFilesListQueryValidator : AbstractValidator<GetFilesListQuery>;

public class GetFilesListQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetFilesListQuery, PagedListDto<StoredFileDto>>
{
    public async Task<PagedListDto<StoredFileDto>> Handle(
        GetFilesListQuery request, CancellationToken cancellationToken)
    {
        var dateRange = request.ToAuditDateRange();
        var paging = request.ToPagingOptions();

        var countSpec = new StoredFileSpecification(request.Id, request.TenantId, request.UserId, request.KeyPrefix,
            request.ContentType, dateRange, PagingOptions.None, OrderingOptions.None, request.Visibility);
        long? totalCount = paging.IncludeTotalCount
            ? await unitOfWork.StoredFileRepository.CountAsync(countSpec, cancellationToken)
            : null;

        var spec = new StoredFileSpecification(request.Id, request.TenantId, request.UserId, request.KeyPrefix,
            request.ContentType, dateRange, paging, request.ToOrderingOptions(), request.Visibility);
        var entities = await unitOfWork.StoredFileRepository.ListAsync(spec, cancellationToken);

        return request.ToPagedListDto(entities, entities.Adapt<List<StoredFileDto>>(), totalCount);
    }
}

public class StoredFileDto : AuditableDto
{
    public Guid Id { get; set; }
    public string? Key { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Checksum { get; set; }
    public bool IsPublic { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}
