using GM.EntityFramework.Domain.Events;
using GM.EntityFramework.Domain.Specifications;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Specifications;

/// <summary>
/// One file's entries in the domain-event log (uploaded, downloaded, deleted, restored, …), filtered by what happened
/// and who did it, then ordered and paged like any list.
/// </summary>
public class StoredFileHistorySpecification : BaseSpecification<StoredDomainEvent>
{
    public StoredFileHistorySpecification(Guid storedFileId, string? action, Guid? userId, Guid? clientId,
        Guid? sessionId, string? ipAddress, AuditDateRange dateRange, PagingOptions paging, OrderingOptions ordering)
    {
        // The log keys each event by its aggregate's type and id (indexed).
        var aggregateId = storedFileId.ToString();
        AddCriteria(e => e.AggregateType == nameof(StoredFile) && e.AggregateId == aggregateId);

        // GM.FileStorage raises GMStoredFile{Action}DomainEvent; downloads recorded before it had one are the
        // sample's own StoredFileDownloadedDomainEvent.
        if (!string.IsNullOrWhiteSpace(action))
        {
            var name = char.ToUpperInvariant(action.Trim()[0]) + action.Trim()[1..];
            var ours = $"StoredFile{name}DomainEvent";
            var library = $"GMStoredFile{name}DomainEvent";
            AddCriteria(e => e.EventType == ours || e.EventType == library);
        }

        if (userId.HasValue)
            AddCriteria(e => e.UserId == userId);

        if (clientId.HasValue)
            AddCriteria(e => e.ClientId == clientId);

        if (sessionId.HasValue)
            AddCriteria(e => e.SessionId == sessionId);

        if (!string.IsNullOrWhiteSpace(ipAddress))
            AddCriteria(e => e.IpAddress == ipAddress);

        // CreatedAtFrom / CreatedAtTo bound when the entry was written, which is when it happened.
        ApplyListQuery(dateRange, paging, ordering);
    }
}
