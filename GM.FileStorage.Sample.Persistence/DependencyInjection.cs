using GM.EntityFramework.Persistence;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.FileStorage.Sample.Persistence.Context;
using GM.FileStorage.Sample.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GM.FileStorage.Sample.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("ApplicationDatabase"),
                o =>
                {
                    o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    o.CommandTimeout(60);
                }).UseSnakeCaseNamingConvention();

            // Stamps CreatedAt/CreatedBy/UpdatedAt/UpdatedBy (and the domain-event actor columns) from GM.API's
            // ambient ICurrentActor.
            options.AddGMActorAuditing(serviceProvider);
        });

        services.AddTransient<IStoredFileRepository, StoredFileRepository>();
        services.AddTransient<IStoredFileMetadataRepository, StoredFileMetadataRepository>();
        services.AddTransient<IUnitOfWork, UnitOfWork.UnitOfWork>();

        // Dispatches domain events raised by aggregates (GMStoredFileUploaded/Replaced/Deleted, …) to their
        // in-process IDomainEventHandler<>s after ApplicationDbContext.SaveChangesAsync persists them. The sample
        // registers no handlers; the events still land in the durable domain_events log.
        services.AddDomainEventDispatcher();

        return services;
    }
}
