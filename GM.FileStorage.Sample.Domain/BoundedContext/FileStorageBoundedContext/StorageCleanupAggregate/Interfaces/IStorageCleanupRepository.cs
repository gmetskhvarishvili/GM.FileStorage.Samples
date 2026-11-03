using GM.EntityFramework.Domain.Repositories;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate.Interfaces;

public interface IStorageCleanupRepository : IGenericRepository<StorageCleanup>;
