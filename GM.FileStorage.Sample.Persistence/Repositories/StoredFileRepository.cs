using GM.EntityFramework.Persistence.Repositories;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;
using GM.FileStorage.Sample.Persistence.Context;

namespace GM.FileStorage.Sample.Persistence.Repositories;

public class StoredFileRepository(ApplicationDbContext context)
    : GenericRepository<StoredFile, ApplicationDbContext>(context), IStoredFileRepository;
