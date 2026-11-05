using GM.EntityFramework.Domain.Events;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Events;

/// <summary>
/// Someone opened the file's content. Lands in the domain-event log with who did it (user, client, session, IP, user
/// agent, correlation id), which is what <c>GET /files/{id}/history</c> reads.
/// </summary>
public sealed record StoredFileDownloadedDomainEvent(Guid StoredFileId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
