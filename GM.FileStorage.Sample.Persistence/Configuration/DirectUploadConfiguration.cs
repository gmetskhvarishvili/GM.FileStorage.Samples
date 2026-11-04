using GM.FileStorage.Domain.FileStorage.StoredFileAggregate.Entities;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.DirectUploadAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GM.FileStorage.Sample.Persistence.Configuration;

// The sample's own aggregate: large files being written straight into storage, not yet recorded.
public class DirectUploadConfiguration : IEntityTypeConfiguration<DirectUpload>
{
    public void Configure(EntityTypeBuilder<DirectUpload> builder)
    {
        builder.ToTable(FileStorageTableNames.DirectUploads, FileStorageTableNames.Schemas.FileStorage);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StorageKey).IsRequired()
            .HasMaxLength(GMStoredFile<StoredFile, StoredFileMetadata>.StorageKeyMaxLength);
        builder.Property(x => x.FileName).IsRequired().HasMaxLength(DirectUpload.FileNameMaxLength);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(DirectUpload.ContentTypeMaxLength);
        builder.Property(x => x.Size).IsRequired();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(DirectUpload.MetadataValueMaxLength);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(DirectUpload.MetadataValueMaxLength);
        builder.Property(x => x.ExpiresAtUtc).IsRequired();

        builder.HasIndex(x => x.StorageKey).IsUnique();
        // The cleanup job removes the ones that expired.
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
