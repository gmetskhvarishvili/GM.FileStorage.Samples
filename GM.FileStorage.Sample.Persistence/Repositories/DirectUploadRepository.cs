using GM.EntityFramework.Persistence.Repositories;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.DirectUploadAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.DirectUploadAggregate.Interfaces;
using GM.FileStorage.Sample.Persistence.Context;

namespace GM.FileStorage.Sample.Persistence.Repositories;

public class DirectUploadRepository(ApplicationDbContext context)
    : GenericRepository<DirectUpload, ApplicationDbContext>(context), IDirectUploadRepository;
