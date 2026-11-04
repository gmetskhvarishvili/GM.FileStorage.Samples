using FluentValidation;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.GetTenantUsage;

/// <summary>How much of its storage quota a tenant uses.</summary>
public class GetTenantUsageQuery : IRequest<TenantUsageDto>
{
    public string? TenantId { get; set; }
}

public class GetTenantUsageQueryValidator : AbstractValidator<GetTenantUsageQuery>
{
    public GetTenantUsageQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().MaximumLength(StoredFileMetadata.ValueMaxLength);
    }
}

public class GetTenantUsageQueryHandler(IUnitOfWork unitOfWork, IOptions<QuotaOptions> quota)
    : IRequestHandler<GetTenantUsageQuery, TenantUsageDto>
{
    public async Task<TenantUsageDto> Handle(GetTenantUsageQuery request, CancellationToken cancellationToken)
    {
        var (used, files) = await TenantQuota.UsageAsync(unitOfWork, request.TenantId!, cancellationToken);
        var limit = quota.Value.BytesPerTenant;
        return new TenantUsageDto
        {
            TenantId = request.TenantId,
            UsedBytes = used,
            QuotaBytes = limit > 0 ? limit : null,
            FileCount = files,
        };
    }
}

/// <summary>A tenant's storage use.</summary>
public class TenantUsageDto
{
    public string? TenantId { get; set; }

    /// <summary>Bytes stored: files (deleted ones until purged) plus direct uploads in progress.</summary>
    public long UsedBytes { get; set; }

    /// <summary>The tenant's quota in bytes; null when unlimited.</summary>
    public long? QuotaBytes { get; set; }

    /// <summary>Files counted, deleted ones included.</summary>
    public int FileCount { get; set; }
}
