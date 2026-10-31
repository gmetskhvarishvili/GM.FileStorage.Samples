using GM.FileStorage.AzureBlob;
using GM.FileStorage.Local;
using GM.FileStorage.S3;
using GM.FileStorage.Sample.Infrastructure.Services;
using GM.FileStorage.Sample.Persistence.Context;
using GM.HealthChecks.EntityFramework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GM.FileStorage.Sample.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // All three backends are registered; FileStorage:Provider (default "Local") decides which one
        // IFileStorageService resolves to — swapping storage is a config change, not a code change. The S3
        // and Azure clients are only constructed when their provider is selected, so the sample runs on
        // disk out of the box with no cloud config.
        services.AddGMFileStorage(configuration);
        services.AddGMLocalFileStorage(configuration);
        services.AddGMS3FileStorage(configuration);
        services.AddGMAzureBlobFileStorage(configuration);

        // KYC-style tenant/user-scoped keys (replaces the default date-partitioned strategy).
        services.AddSingleton<IFileKeyStrategy, TenantKeyStrategy>();

        // Readiness: /health/ready fails while the catalog database is unreachable.
        services.AddHealthChecks()
            .AddGMDatabaseCheck<ApplicationDbContext>();

        return services;
    }
}
