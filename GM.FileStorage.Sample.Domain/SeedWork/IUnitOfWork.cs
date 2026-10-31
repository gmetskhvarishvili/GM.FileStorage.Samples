using GM.EntityFramework.Domain.Repositories;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate.Interfaces;

namespace GM.FileStorage.Sample.Domain.SeedWork;

public interface IUnitOfWork : IGenericUnitOfWork
{
    public IStoredFileRepository StoredFileRepository { get; }
    public IStoredFileMetadataRepository StoredFileMetadataRepository { get; }
}
