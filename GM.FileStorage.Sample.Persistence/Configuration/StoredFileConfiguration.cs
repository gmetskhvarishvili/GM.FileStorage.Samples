using GM.FileStorage.Persistence.Configuration;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GM.FileStorage.Sample.Persistence.Configuration;

// The stored-file mapping lives in the GM.FileStorage base config; this fixes it to the sample's
// aggregate (named by FileStorageTableNames, without GM.FileStorage's gm_ prefix) and adds the sample's own columns.
public class StoredFileConfiguration : GMStoredFileConfiguration<StoredFile>
{
    protected override string Schema => FileStorageTableNames.Schemas.FileStorage;
    protected override string Table => FileStorageTableNames.StoredFiles;

    public override void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        base.Configure(builder);

        builder.Property(x => x.IntegrityProblem).HasMaxLength(StoredFile.IntegrityProblemMaxLength);

        // Every query is scoped to a tenant.
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(StoredFile.TenantIdMaxLength);
        builder.HasIndex(x => x.TenantId);

        // The retention job finds files deleted long ago; the integrity job, those checked least recently.
        builder.HasIndex(x => x.DeletedAtUtc);
        builder.HasIndex(x => x.VerifiedAtUtc);
    }
}
