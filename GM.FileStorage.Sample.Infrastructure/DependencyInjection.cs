using GM.FileStorage.AzureBlob;
using GM.FileStorage.Encryption;
using GM.FileStorage.Local;
using GM.FileStorage.S3;
using GM.Caching;
using GM.Caching.Redis;
using GM.DistributedLock;
using GM.DistributedLock.Redis;
using GM.FileStorage.Sample.Application.Common;
using GM.RateLimiting;
using GM.RateLimiting.Redis;
using GM.FileStorage.Sample.Infrastructure.Jobs;
using GM.FileStorage.Sample.Persistence.Context;
using GM.HealthChecks.EntityFramework;
using GM.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Large files written straight into storage through presigned links (DirectUploads in appsettings.json).
        services.Configure<DirectUploadOptions>(configuration.GetSection("DirectUploads"));

        // Bytes each tenant may store (Quotas:BytesPerTenant).
        services.Configure<QuotaOptions>(configuration.GetSection("Quotas"));

        // Uploaded files are counted per client (RateLimiting:Policies in appsettings.json), across instances when
        // Redis is configured: the gateway limits requests, but a batch holds up to 20 files.
        var redis = AddCoordination(services, configuration);
        services.AddGMRateLimiting(configuration.GetSection("RateLimiting"));
        if (redis)
            services.AddGMRedisRateLimitStore();

        // Readiness: /health/ready fails while the catalog database is unreachable.
        services.AddHealthChecks()
            .AddGMDatabaseCheck<ApplicationDbContext>();

        return services;
    }

    /// <summary>
    /// The worker: file storage plus the scheduled jobs. <see cref="StorageCleanupJob"/> deletes whatever bytes uploads
    /// leave unreferenced; <see cref="IntegrityCheckJob"/> reads stored files back against the catalog;
    /// <see cref="RetentionJob"/> purges files deleted long ago. The integrity check reads file contents, so the worker
    /// needs the same encryption settings and keys as the API, and must see the same storage (FileStorage settings and
    /// root).
    /// </summary>
    public static IServiceCollection AddWorkerInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        AddFileStorage(services, configuration);
        services.AddGMFileStorageEncryption(configuration);

        // One run per minute cluster-wide, guarded by the distributed lock: in-memory for a single worker, Redis (when
        // configured) so several workers never clean at the same time.
        AddCoordination(services, configuration);
        services.Configure<IntegrityOptions>(configuration.GetSection("Integrity"));
        services.Configure<RetentionOptions>(configuration.GetSection("Retention"));
        services.AddGMScheduling(builder => builder.ScanAssemblies(typeof(StorageCleanupJob).Assembly));

        return services;
    }

    /// <summary>
    /// The cache and distributed lock that rate limiting and scheduling build on: Redis when
    /// <c>Redis:ConnectionString</c> is set, so every instance shares them, otherwise in-memory (one instance). Returns
    /// whether Redis is used.
    /// </summary>
    public static bool AddCoordination(IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["Redis:ConnectionString"]))
        {
            services.AddGMCaching();
            services.AddGMDistributedLock();
            return false;
        }

        // GM.API registers in-memory ones first (it looks for ConnectionStrings:Redis, not this section), and these
        // registrations only fill gaps, so remove those before adding Redis.
        services.RemoveAll<ICacheService>();
        services.RemoveAll<IDistributedLock>();
        services.AddGMRedisCaching(configuration);
        services.AddGMRedisDistributedLock(configuration);
        return true;
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
