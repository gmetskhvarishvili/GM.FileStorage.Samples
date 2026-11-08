using GM.FileStorage.Sample.Domain.BoundedContext.AuditBoundedContext.AuditAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GM.FileStorage.Sample.Persistence.Configuration;

/// <summary>
/// Maps <see cref="DomainEventLog"/> as a read-only, keyless projection over the domain-event log table
/// (owned/written by the persistence library's StoredDomainEvent). Mapping it as a view keeps it queryable
/// while excluding it from migrations, so there is no second, conflicting table mapping.
/// </summary>
public class DomainEventLogConfiguration : IEntityTypeConfiguration<DomainEventLog>
{
    public void Configure(EntityTypeBuilder<DomainEventLog> builder)
    {
        builder.HasNoKey();
        // Explicitly table-less: otherwise the snake_case naming convention leaves it with the model's default
        // schema but no table name, which EF's snapshot generator writes as ToTable(null, …), and the snapshot
        // then throws on load (Migrate / has-pending-model-changes: "Value cannot be null (Parameter 'name')").
        builder.ToTable((string?)null);
        // The same table the log is written to (see StoredDomainEventStoreConfiguration).
        builder.ToView(StoredDomainEventStoreConfiguration.TableName, StoredDomainEventStoreConfiguration.SchemaName);
    }
}
