using GM.EntityFramework.Domain.Repositories;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;

public interface IStoredFileRepository : IGenericRepository<StoredFile>;
