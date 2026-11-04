using GM.FileStorage.Domain.FileStorage.StoredFileAggregate.Entities;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GM.FileStorage.Sample.Persistence.Configuration;

// The sample's own aggregate (not a GM.FileStorage one): the queue of storage objects to delete.
public class StorageCleanupConfiguration : IEntityTypeConfiguration<StorageCleanup>
{
    public void Configure(EntityTypeBuilder<StorageCleanup> builder)
    {
        builder.ToTable(FileStorageTableNames.StorageCleanups, FileStorageTableNames.Schemas.FileStorage);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StorageKey).IsRequired()
            .HasMaxLength(GMStoredFile<StoredFile>.StorageKeyMaxLength);
        // Claiming an entry moves DueAtUtc; as a concurrency token it makes the claim and an upload's own removal
        // of its pending entry mutually exclusive.
        builder.Property(x => x.DueAtUtc).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.Attempts).IsRequired();

        // The cleanup job reads what's due.
        builder.HasIndex(x => x.DueAtUtc);
        builder.HasIndex(x => x.StorageKey);
    }
}
