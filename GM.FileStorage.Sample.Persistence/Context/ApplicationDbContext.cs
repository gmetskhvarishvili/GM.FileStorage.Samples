using GM.EntityFramework.Domain.Events;
using GM.EntityFramework.Persistence;
using Microsoft.EntityFrameworkCore;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Persistence.Context;

public class ApplicationDbContext : GenericDbContext
{
    public const string DefaultSchema = "application";

    private readonly IDomainEventDispatcher _dispatcher;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IDomainEventDispatcher dispatcher) : base(options)
    {
        _dispatcher = dispatcher;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(DefaultSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot the raised domain events before persisting: the base save copies each to the durable
        // DomainEvents log and clears the aggregates' in-memory lists. We then dispatch to the in-process
        // handlers once the changes — event rows included — are saved.
        var domainEvents = ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(entry => entry.Entity.DomainEvents)
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        if (domainEvents.Count > 0)
        {
            await _dispatcher.DispatchRangeAsync(domainEvents, cancellationToken);
        }

        return result;
    }
}
