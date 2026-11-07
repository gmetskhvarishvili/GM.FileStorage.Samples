using GM.EntityFramework.Domain.Events;
using GM.EntityFramework.Domain.Specifications;
using GM.EntityFramework.Persistence.Repositories;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;
using GM.FileStorage.Sample.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Persistence.Repositories;

public class StoredFileRepository(ApplicationDbContext context)
    : GenericRepository<StoredFile, ApplicationDbContext>(context), IStoredFileRepository
{
    public Task<List<StoredDomainEvent>> ListHistoryAsync(ISpecification<StoredDomainEvent> specification,
        CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.GetQuery(_context.DomainEvents.AsNoTracking(), specification).ToListAsync(cancellationToken);

    public Task<int> CountHistoryAsync(ISpecification<StoredDomainEvent> specification,
        CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.GetQuery(_context.DomainEvents.AsNoTracking(), specification).CountAsync(cancellationToken);
}
