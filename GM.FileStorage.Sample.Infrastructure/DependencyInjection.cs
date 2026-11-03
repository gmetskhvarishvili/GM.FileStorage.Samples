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
    /// <summary>The API: file storage (with encryption at rest when switched on) and its readiness check.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        AddFileStorage(services, configuration);

        // Encryption at rest, off in appsettings.json (FileStorage:Encryption:Enabled). Turn it on together with the
        // master keys, supplied as secrets (appsecrets.json, SECRETS_PROVIDER, environment): every file is then
        // encrypted with its own AES-256-GCM key, wrapped by FileStorage:Encryption:Keys[ActiveKeyId].
        services.AddGMFileStorageEncryption(configuration);

        // Readiness: /health/ready fails while the catalog database is unreachable.
        services.AddHealthChecks()
            .AddGMDatabaseCheck<ApplicationDbContext>();

        return services;
    }

    /// <summary>
    /// The worker: file storage plus the scheduled <see cref="StorageCleanupJob"/>, which deletes whatever bytes
    /// uploads leave unreferenced (crashes and failures between storing the bytes and recording them). It only deletes objects, so it needs
    /// no encryption keys. It must see the same storage as the API (same FileStorage settings and root).
    /// </summary>
    public static IServiceCollection AddWorkerInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        AddFileStorage(services, configuration);

        // One run per minute cluster-wide: the in-memory lock suits a single worker; use GM.DistributedLock.Redis when
        // running several.
        services.AddGMDistributedLock();
        services.AddGMScheduling(builder => builder.ScanAssemblies(typeof(StorageCleanupJob).Assembly));

        return services;
    }

    // All three backends are registered; FileStorage:Provider (default "Local") decides which one IFileStorageService
    // resolves to — swapping storage is a config change, not a code change. The S3 and Azure clients are only
    // constructed when their provider is selected, so the sample runs on disk out of the box with no cloud config.
    // Generated storage keys follow FileStorage:KeyTemplate ({category}/{shard}/{id}{ext}).
    private static void AddFileStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGMFileStorage(configuration);
        services.AddGMLocalFileStorage(configuration);
        services.AddGMS3FileStorage(configuration);
        services.AddGMAzureBlobFileStorage(configuration);
    }
}
