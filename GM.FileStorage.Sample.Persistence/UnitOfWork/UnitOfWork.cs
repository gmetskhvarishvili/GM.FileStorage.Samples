using GM.EntityFramework.Persistence;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.FileStorage.Sample.Persistence.Context;

namespace GM.FileStorage.Sample.Persistence.UnitOfWork;

public sealed class UnitOfWork(
    ApplicationDbContext context,
    IStoredFileRepository storedFileRepository,
    IStorageCleanupRepository storageCleanupRepository)
    : GenericUnitOfWork<ApplicationDbContext>(context), IUnitOfWork
{
    public IStoredFileRepository StoredFileRepository { get; } = storedFileRepository;
    public IStorageCleanupRepository StorageCleanupRepository { get; } = storageCleanupRepository;
}
