using GM.EntityFramework.Domain.Specifications;
using GM.FileStorage.Sample.Domain.SeedWork;
using System;
using System.Linq;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Specifications;

public class StoredFileSpecification : BaseSpecification<StoredFile>
{
    public StoredFileSpecification(Guid? id, string? tenantId, Guid? userId, string? contentType,
        AuditDateRange dateRange, PagingOptions paging, OrderingOptions ordering,
        VisibilityScope visibility = VisibilityScope.VisibleOnly, bool? integrityProblem = null)
    {
        AddVisibilityFilter(visibility);

        if (id.HasValue && id.Value != Guid.Empty)
            AddCriteria(f => f.Id == id);

        // The tenant is a column (indexed); the uploader is the audit column CreatedBy.
        if (!string.IsNullOrWhiteSpace(tenantId))
            AddCriteria(f => f.TenantId == tenantId);

        if (userId.HasValue)
            AddCriteria(f => f.CreatedBy == userId);

        if (!string.IsNullOrWhiteSpace(contentType))
            AddCriteria(f => f.ContentType == contentType);

        // Files whose last integrity check found a problem (true), or none (false).
        if (integrityProblem == true)
            AddCriteria(f => f.IntegrityProblem != null);
        else if (integrityProblem == false)
            AddCriteria(f => f.IntegrityProblem == null);

        // The list returns each file's metadata alongside it.

        ApplyListQuery(dateRange, paging, ordering);
    }
}
