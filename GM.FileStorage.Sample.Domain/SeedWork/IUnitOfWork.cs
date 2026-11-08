using GM.EntityFramework.Domain.Repositories;
using GM.FileStorage.Sample.Domain.BoundedContext.AuditBoundedContext.AuditAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;

namespace GM.FileStorage.Sample.Domain.SeedWork;

public interface IUnitOfWork : IGenericUnitOfWork
{
    public IStoredFileRepository StoredFileRepository { get; }
    public IStorageCleanupRepository StorageCleanupRepository { get; }
    public IDomainEventLogRepository DomainEventLogRepository { get; }
}
