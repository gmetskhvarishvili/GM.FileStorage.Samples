using GM.FileStorage.AzureBlob;
using GM.FileStorage.Encryption;
using GM.FileStorage.Local;
using GM.FileStorage.S3;
using GM.DistributedLock;
using GM.FileStorage.Sample.Infrastructure.Jobs;
using GM.FileStorage.Sample.Persistence.Context;
using GM.HealthChecks.EntityFramework;
using GM.Scheduling;
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
        // disk out of the box with no cloud config. Generated keys follow FileStorage:KeyTemplate
        // ({category}/{shard}/{id}{ext}, e.g. documents/9f/9f1c….pdf); tenant and user stay as searchable metadata.
        services.AddGMFileStorage(configuration);
        services.AddGMLocalFileStorage(configuration);
        services.AddGMS3FileStorage(configuration);
        services.AddGMAzureBlobFileStorage(configuration);

        // Encryption at rest, off in appsettings.json (FileStorage:Encryption:Enabled). Turn it on together with the
        // master keys, supplied as secrets (appsecrets.json, SECRETS_PROVIDER, environment): every file is then
        // encrypted with its own AES-256-GCM key, wrapped by FileStorage:Encryption:Keys[ActiveKeyId].
        services.AddGMFileStorageEncryption(configuration);

        // StorageCleanupJob deletes whatever bytes uploads and deletes leave unreferenced (crashes, failed deletes,
        // replaced versions). The in-memory lock suits one instance; use GM.DistributedLock.Redis when scaling out
        // so the job still runs once per minute cluster-wide.
        services.AddGMDistributedLock();
        services.AddGMScheduling(builder => builder.ScanAssemblies(typeof(StorageCleanupJob).Assembly));

        // Readiness: /health/ready fails while the catalog database is unreachable.
        services.AddHealthChecks()
            .AddGMDatabaseCheck<ApplicationDbContext>();

        return services;
    }
}
