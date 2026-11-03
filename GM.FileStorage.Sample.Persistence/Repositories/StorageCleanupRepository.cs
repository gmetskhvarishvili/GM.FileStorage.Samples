using GM.EntityFramework.Persistence.Repositories;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate.Interfaces;
using GM.FileStorage.Sample.Persistence.Context;

namespace GM.FileStorage.Sample.Persistence.Repositories;

public class StorageCleanupRepository(ApplicationDbContext context)
    : GenericRepository<StorageCleanup, ApplicationDbContext>(context), IStorageCleanupRepository;
