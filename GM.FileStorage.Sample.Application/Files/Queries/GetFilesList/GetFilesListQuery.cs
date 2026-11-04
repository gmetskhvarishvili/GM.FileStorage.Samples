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

    /// <summary>Only files stored under this folder, e.g. <c>images/</c> for every image.</summary>
    public string? Folder { get; set; }

    public string? ContentType { get; set; }

    /// <summary>Only files whose last integrity check found a problem (true), or none (false).</summary>
    public bool? IntegrityProblem { get; set; }
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

        var countSpec = new StoredFileSpecification(request.Id, request.TenantId, request.UserId, request.Folder,
            request.ContentType, dateRange, PagingOptions.None, OrderingOptions.None, request.Visibility, request.IntegrityProblem);
        long? totalCount = paging.IncludeTotalCount
            ? await unitOfWork.StoredFileRepository.CountAsync(countSpec, cancellationToken)
            : null;

        var spec = new StoredFileSpecification(request.Id, request.TenantId, request.UserId, request.Folder,
            request.ContentType, dateRange, paging, request.ToOrderingOptions(), request.Visibility, request.IntegrityProblem);
        var entities = await unitOfWork.StoredFileRepository.ListAsync(spec, cancellationToken);

        return request.ToPagedListDto(entities, entities.Adapt<List<StoredFileDto>>(), totalCount);
    }
}

public class StoredFileDto : AuditableDto
{
    public Guid Id { get; set; }
    /// <summary>The stored object's name, e.g. <c>9f1c….pdf</c>.</summary>
    public string? StorageKey { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Checksum { get; set; }
    public bool IsPublic { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }

    /// <summary>When the stored bytes were last checked against this record (UTC).</summary>
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>What the last check found wrong with the stored bytes; null when they matched.</summary>
    public string? IntegrityProblem { get; set; }
}
