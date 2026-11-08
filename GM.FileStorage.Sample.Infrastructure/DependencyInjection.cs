using GM.FileStorage.MalwareScanner;
using GM.Caching.Redis;
using GM.DistributedLock.Redis;
using GM.FileStorage.Catalog.Scheduling;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.RateLimiting;
using GM.RateLimiting.Redis;
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
        // One call registers every GM.FileStorage package this project references whose FileStorage settings are present:
        // - the Local, S3 and Azure backends (their sections are in appsettings.json). FileStorage:Provider (default "Local")
        //   decides which one IFileStorageService resolves to — swapping storage is a config change, not a code change.
        //   The S3 and Azure clients are only constructed when their provider is selected, so the sample runs on disk out
        //   of the box with no cloud config.
        // - encryption at rest (FileStorage:Encryption:Enabled, master keys from secrets): every file encrypted with its
        //   own AES-256-GCM key, wrapped by FileStorage:Encryption:Keys[ActiveKeyId].
        // - malware scanning with ClamAV (off unless FileStorage:MalwareScanner:Enabled), after encryption, so it sees the
        //   plaintext: an infected upload is refused, and so is one ClamAV can't answer for.
        // Generated storage keys follow FileStorage:KeyTemplate ({meta:tenantId}/{date:yyyy}/{date:MM}/{date:dd}/{id}).
        services.AddGMFileStorage(configuration);

        // Bytes each tenant may store (Quotas:BytesPerTenant).
        services.Configure<QuotaOptions>(configuration.GetSection("Quotas"));

        // Uploaded files are counted per client (RateLimiting:Policies in appsettings.json): the gateway limits requests,
        // but a batch holds up to 20 files. The cache, lock and counts are in Redis when Redis:ConnectionString is set,
        // so every instance shares them, and in memory otherwise (one instance).
        services.AddGMCaching(configuration);
        services.AddGMDistributedLock(configuration);
        services.AddGMRateLimiting(configuration.GetSection("RateLimiting"));
        services.AddGMRateLimitStore(configuration);

        // Readiness: /health/ready fails while the catalog database is unreachable, or ClamAV while scanning is on.
        services.AddHealthChecks()
            .AddGMDatabaseCheck<ApplicationDbContext>()
            .AddGMFileStorageMalwareScannerCheck();

        return services;
    }

    /// <summary>
    /// The worker: file storage plus GM.FileStorage.Catalog's scheduled jobs. storage-cleanup deletes whatever bytes
    /// uploads leave unreferenced; integrity-check reads stored files back against the catalog; retention purges files
    /// deleted long ago. The integrity check reads file contents, so the worker
    /// needs the same encryption settings and keys as the API, and must see the same storage (FileStorage settings and
    /// root).
    /// </summary>
    public static IServiceCollection AddWorkerInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // The same storage, encryption and scanning as the API (see AddInfrastructure). When scanning is on, the integrity
        // check also rescans what it reads, catching files that match signatures added since they were uploaded.
        services.AddGMFileStorage(configuration);

        // Each job runs once cluster-wide, guarded by the distributed lock: in memory for a single worker, Redis (when
        // Redis:ConnectionString is set) so several workers never run one at the same time. Batch sizes and retention
        // come from FileStorage:Catalog, which AddGMFileStorage binds.
        services.AddGMDistributedLock(configuration);
        services.AddGMScheduling(builder => builder.AddFileStorageCatalogJobs<StoredFile, StorageCleanup>());

        return services;
    }
}
