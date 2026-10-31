using GM.FileStorage.Persistence.Configuration;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;

namespace GM.FileStorage.Sample.Persistence.Configuration;

public class StoredFileMetadataConfiguration : GMStoredFileMetadataConfiguration<StoredFile, StoredFileMetadata>
{
    protected override string Schema => FileStorageTableNames.Schemas.FileStorage;
    protected override string Table => FileStorageTableNames.StoredFileMetadata;
}
