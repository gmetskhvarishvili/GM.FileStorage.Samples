using GM.EntityFramework.Domain.Specifications;
using GM.FileStorage.Sample.Domain.SeedWork;
using System;
using System.Linq;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Specifications;

public class StoredFileSpecification : BaseSpecification<StoredFile>
{
    public StoredFileSpecification(Guid? id, string? tenantId, string? userId, string? keyPrefix, string? contentType,
        AuditDateRange dateRange, PagingOptions paging, OrderingOptions ordering,
        VisibilityScope visibility = VisibilityScope.VisibleOnly)
    {
        AddVisibilityFilter(visibility);

        if (id.HasValue && id.Value != Guid.Empty)
            AddCriteria(f => f.Id == id);

        // Tenant and user are metadata rows, so these are EXISTS sub-queries served by the (name, value) index.
        if (!string.IsNullOrWhiteSpace(tenantId))
            AddCriteria(f => f.Metadata.Any(m =>
                m.Name == StoredFileMetadataNames.TenantId && m.Value == tenantId && !m.IsDeleted));

        if (!string.IsNullOrWhiteSpace(userId))
            AddCriteria(f => f.Metadata.Any(m =>
                m.Name == StoredFileMetadataNames.UserId && m.Value == userId && !m.IsDeleted));

        if (!string.IsNullOrWhiteSpace(keyPrefix))
            AddCriteria(f => f.Key.StartsWith(keyPrefix));

        if (!string.IsNullOrWhiteSpace(contentType))
            AddCriteria(f => f.ContentType == contentType);

        // The list returns each file's metadata alongside it.
        AddInclude(f => f.Metadata);

        ApplyListQuery(dateRange, paging, ordering);
    }
}
