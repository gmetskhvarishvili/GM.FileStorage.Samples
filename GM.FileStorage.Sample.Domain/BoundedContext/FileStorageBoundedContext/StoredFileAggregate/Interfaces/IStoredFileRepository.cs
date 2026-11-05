using GM.EntityFramework.Domain.Events;
using GM.EntityFramework.Domain.Repositories;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;

public interface IStoredFileRepository : IGenericRepository<StoredFile>
{
    /// <summary>
    /// What happened to a file, newest first, at most <paramref name="limit"/> entries: its events from the
    /// domain-event log (uploaded, downloaded, deleted, restored, …), each with who did it.
    /// </summary>
    Task<List<StoredDomainEvent>> GetHistoryAsync(Guid storedFileId, int limit, CancellationToken cancellationToken = default);
}
