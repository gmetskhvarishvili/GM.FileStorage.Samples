using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.AuditBoundedContext.AuditAggregate;

/// <summary>
/// The audit-log aggregate: a read-only projection of one persisted domain event, including the actor/request
/// metadata stamped by the auditing context. Mapped read-only (a keyless view over the domain-events table —
/// the log is append-only, owned by the persistence layer) and queried with specifications, such as a file's
/// history (<c>StoredFileHistorySpecification</c>).
/// </summary>
public class DomainEventLog
{
    public Guid Id { get; set; }
    public string? AggregateType { get; set; }
    public string? AggregateId { get; set; }
    public string EventType { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTime OccurredOn { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? SessionId { get; set; }
    public string? ChannelId { get; set; }
    public string? Culture { get; set; }
    public string? Source { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
    public string? UserAgent { get; set; }
    public string? IdempotencyKey { get; set; }
}
