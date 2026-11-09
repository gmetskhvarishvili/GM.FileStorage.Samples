using GM.EntityFramework.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GM.FileStorage.Sample.Persistence.Context;

public class ApplicationDbContext : GenericDbContext
{
    public const string DefaultSchema = "application";

    // GenericDbContext logs the domain events aggregates raise (GMStoredFileUploaded/Deleted/Restored, …) with each
    // save, and dispatches them to their IDomainEventHandler<>s after SaveChangesAsync. The sample registers no
    // handlers; the events still land in the durable domain_events log.
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(DefaultSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
