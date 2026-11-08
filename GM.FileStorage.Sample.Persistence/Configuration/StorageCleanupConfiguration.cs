using GM.FileStorage.Persistence.Configuration;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;

namespace GM.FileStorage.Sample.Persistence.Configuration;

// The cleanup-queue mapping lives in the GM.FileStorage base config; this fixes it to the sample's
// aggregate, named by FileStorageTableNames (without GM.FileStorage's gm_ prefix).
public class StorageCleanupConfiguration : GMStorageCleanupConfiguration<StorageCleanup>
{
    protected override string Schema => FileStorageTableNames.Schemas.FileStorage;
    protected override string Table => FileStorageTableNames.StorageCleanups;
}
