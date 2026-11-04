using GM.EntityFramework.Persistence;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.DirectUploadAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.FileStorage.Sample.Persistence.Context;

namespace GM.FileStorage.Sample.Persistence.UnitOfWork;

public sealed class UnitOfWork(
    ApplicationDbContext context,
    IStoredFileRepository storedFileRepository,
    IStoredFileMetadataRepository storedFileMetadataRepository,
    IStorageCleanupRepository storageCleanupRepository,
    IDirectUploadRepository directUploadRepository)
    : GenericUnitOfWork<ApplicationDbContext>(context), IUnitOfWork
{
    public IStoredFileRepository StoredFileRepository { get; } = storedFileRepository;
    public IStoredFileMetadataRepository StoredFileMetadataRepository { get; } = storedFileMetadataRepository;
    public IStorageCleanupRepository StorageCleanupRepository { get; } = storageCleanupRepository;
    public IDirectUploadRepository DirectUploadRepository { get; } = directUploadRepository;
}
