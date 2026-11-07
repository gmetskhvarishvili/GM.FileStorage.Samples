using GM.EntityFramework.Domain.Events;
using GM.EntityFramework.Domain.Repositories;
using GM.EntityFramework.Domain.Specifications;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;

public interface IStoredFileRepository : IGenericRepository<StoredFile>
{
    /// <summary>
    /// A page of what happened to a file: its events from the domain-event log (uploaded, downloaded, deleted,
    /// restored, …), each with who did it, as the specification filters, orders and pages them.
    /// </summary>
    Task<List<StoredDomainEvent>> ListHistoryAsync(ISpecification<StoredDomainEvent> specification,
        CancellationToken cancellationToken = default);

    /// <summary>How many of a file's events the specification matches.</summary>
    Task<int> CountHistoryAsync(ISpecification<StoredDomainEvent> specification,
        CancellationToken cancellationToken = default);
}
