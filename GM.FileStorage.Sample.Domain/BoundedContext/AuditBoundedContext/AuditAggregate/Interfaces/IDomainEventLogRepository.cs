using GM.EntityFramework.Domain.Repositories;

namespace GM.FileStorage.Sample.Domain.BoundedContext.AuditBoundedContext.AuditAggregate.Interfaces;

/// <summary>
/// Read-only access to the audit log (the append-only domain-event log). Query with a specification via
/// <c>ListAsync</c>/<c>CountAsync</c>; the write-side methods of the generic repository are unused (the log is
/// written only by the auditing context).
/// </summary>
public interface IDomainEventLogRepository : IGenericRepository<DomainEventLog>;
