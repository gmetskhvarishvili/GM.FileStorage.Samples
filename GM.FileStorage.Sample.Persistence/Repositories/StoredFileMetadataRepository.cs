using GM.EntityFramework.Persistence.Repositories;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate.Interfaces;
using GM.FileStorage.Sample.Persistence.Context;

namespace GM.FileStorage.Sample.Persistence.Repositories;

public class StoredFileMetadataRepository(ApplicationDbContext context)
    : GenericRepository<StoredFileMetadata, ApplicationDbContext>(context), IStoredFileMetadataRepository;
