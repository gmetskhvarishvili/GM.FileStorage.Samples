using GM.Exceptions;
using GM.FileStorage.Sample.Domain.SeedWork;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Common;

/// <summary>Storage quotas (<c>Quotas</c> in appsettings.json).</summary>
public class QuotaOptions
{
    /// <summary>The most bytes one tenant may store. Default 1 GB; 0 means unlimited.</summary>
    public long BytesPerTenant { get; set; } = 1024L * 1024 * 1024;
}

/// <summary>
/// How much storage a tenant uses, and the check uploads make against <see cref="QuotaOptions.BytesPerTenant"/>.
/// Usage is everything that occupies storage for the tenant: its files, deleted ones included (their bytes are kept
/// until retention purges them). The check is made
/// per upload, so two uploads racing can go slightly over; it's a guard, not an exact meter.
/// </summary>
public static class TenantQuota
{
    /// <summary>The tenant's stored bytes, and how many files they are.</summary>
    public static async Task<(long UsedBytes, int FileCount)> UsageAsync(IUnitOfWork unitOfWork, string tenantId, CancellationToken cancellationToken)
    {
        var files = unitOfWork.StoredFileRepository.Query(asNoTracking: true)
            .Where(f => f.TenantId == tenantId);
        var fileBytes = await files.SumAsync(f => (long?)f.Size, cancellationToken) ?? 0;
        var fileCount = await files.CountAsync(cancellationToken);
        return (fileBytes, fileCount);
    }

    /// <summary>Throws a 400 when <paramref name="incomingBytes"/> more would take the tenant past its quota.</summary>
    public static async Task EnsureRoomAsync(
        IUnitOfWork unitOfWork, QuotaOptions options, string tenantId, long incomingBytes, CancellationToken cancellationToken)
    {
        if (options.BytesPerTenant <= 0)
            return;

        var (used, _) = await UsageAsync(unitOfWork, tenantId, cancellationToken);
        if (used + incomingBytes > options.BytesPerTenant || (incomingBytes == 0 && used >= options.BytesPerTenant))
        {
            throw new BadRequestException(
                $"Tenant '{tenantId}' would go past its {options.BytesPerTenant}-byte storage quota ({used} bytes used). " +
                "Delete files (they count until purged) or ask for a larger quota.");
        }
    }
}
