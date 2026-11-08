using GM.EntityFramework.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GM.FileStorage.Sample.Domain.BoundedContext.AuditBoundedContext.AuditAggregate.Specifications;

/// <summary>
/// Filters the audit log by the aggregate an event belongs to, its event type and the actor/request metadata (every
/// argument is optional), then orders and pages it like any list. A file's history is the log of one aggregate
/// (<c>StoredFile</c> and its id).
/// </summary>
public class DomainEventLogSpecification : BaseSpecification<DomainEventLog>
{
    public DomainEventLogSpecification(
        string? aggregateType,
        string? aggregateId,
        IReadOnlyCollection<string>? eventTypes,
        Guid? userId,
        Guid? clientId,
        Guid? sessionId,
        string? ipAddress,
        AuditDateRange dateRange,
        PagingOptions paging,
        OrderingOptions ordering)
    {
        // The log keys each event by its aggregate's type and id (indexed).
        if (!string.IsNullOrWhiteSpace(aggregateType))
            AddCriteria(e => e.AggregateType == aggregateType);

        if (!string.IsNullOrWhiteSpace(aggregateId))
            AddCriteria(e => e.AggregateId == aggregateId);

        // Any of these types: one action can be recorded under more than one name.
        if (eventTypes is { Count: > 0 })
            AddCriteria(e => eventTypes.Contains(e.EventType));

        if (userId.HasValue && userId.Value != Guid.Empty)
            AddCriteria(e => e.UserId == userId);

        if (clientId.HasValue && clientId.Value != Guid.Empty)
            AddCriteria(e => e.ClientId == clientId);

        if (sessionId.HasValue && sessionId.Value != Guid.Empty)
            AddCriteria(e => e.SessionId == sessionId);

        if (!string.IsNullOrWhiteSpace(ipAddress))
            AddCriteria(e => e.IpAddress == ipAddress);

        // CreatedAtFrom / CreatedAtTo bound when the entry was written, which is when it happened.
        ApplyListQuery(dateRange, paging, ordering);
    }
}
