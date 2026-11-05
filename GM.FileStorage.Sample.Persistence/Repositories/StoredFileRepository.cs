using GM.EntityFramework.Domain.Events;
using GM.EntityFramework.Persistence.Repositories;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;
using GM.FileStorage.Sample.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Persistence.Repositories;

public class StoredFileRepository(ApplicationDbContext context)
    : GenericRepository<StoredFile, ApplicationDbContext>(context), IStoredFileRepository
{
    public Task<List<StoredDomainEvent>> GetHistoryAsync(Guid storedFileId, int limit, CancellationToken cancellationToken = default)
    {
        // The log keys each event by its aggregate's type and id, so this is an indexed lookup.
        var aggregateId = storedFileId.ToString();
        return _context.DomainEvents
            .AsNoTracking()
            .Where(e => e.AggregateType == nameof(StoredFile) && e.AggregateId == aggregateId)
            .OrderByDescending(e => e.OccurredOn)
            .ThenByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
