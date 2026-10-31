using GM.EntityFramework.Persistence.Configuration;

namespace GM.FileStorage.Sample.Persistence.Configuration;

/// <summary>
/// Maps GM.EntityFramework's domain-event log into the sample's snake_case naming. The base configuration
/// (applied by GenericDbContext) keeps the column mapping and indexes; this overrides only where the table lives.
/// Picked up by ApplyConfigurationsFromAssembly, which runs after base.OnModelCreating, so these names win.
/// </summary>
public class StoredDomainEventStoreConfiguration : StoredDomainEventConfiguration
{
    public const string SchemaName = "domain_events";
    public const string TableName = "domain_events";

    protected override string Schema => SchemaName;
    protected override string Table => TableName;
}
