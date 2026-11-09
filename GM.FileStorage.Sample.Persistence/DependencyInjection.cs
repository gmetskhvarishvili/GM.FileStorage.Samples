using GM.EntityFramework.Persistence;
using GM.FileStorage.Persistence;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.AuditBoundedContext.AuditAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate.Interfaces;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate.Interfaces;
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
        services.AddTransient<IStorageCleanupRepository, StorageCleanupRepository>();
        services.AddTransient<IDomainEventLogRepository, DomainEventLogRepository>();
        services.AddTransient<IUnitOfWork, UnitOfWork.UnitOfWork>();

        // GM.FileStorage.Catalog: cleanup, purge, retention and integrity checks over ApplicationDbContext, sharing each
        // request's context with the sample's own handlers.
        services.AddGMFileStorageCatalog<ApplicationDbContext, StoredFile, StorageCleanup>();

        return services;
    }
}
