using GM.FileStorage.Persistence.Configuration;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;

namespace GM.FileStorage.Sample.Persistence.Configuration;

// The stored-file mapping lives in the GM.FileStorage base config; this fixes it to the sample's
// aggregate (named by FileStorageTableNames, without GM.FileStorage's gm_ prefix).
public class StoredFileConfiguration : GMStoredFileConfiguration<StoredFile, StoredFileMetadata>
{
    protected override string Schema => FileStorageTableNames.Schemas.FileStorage;
    protected override string Table => FileStorageTableNames.StoredFiles;
}
