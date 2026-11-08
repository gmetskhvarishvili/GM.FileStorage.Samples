using GM.FileStorage.Catalog;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.Scheduling;
using Microsoft.Extensions.Options;
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Infrastructure.Jobs;

/// <summary>The retention job's settings (<c>Retention</c> in the worker's appsettings.json).</summary>
public class RetentionOptions
{
    /// <summary>
    /// How long a deleted file can still be restored, in days; after that it's purged, bytes and all. 0 keeps deleted
    /// files forever.
    /// </summary>
    public int DeletedFileDays { get; set; } = 30;

    /// <summary>Files purged per run (every hour).</summary>
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Purges files deleted longer ago than <see cref="RetentionOptions.DeletedFileDays"/>, through GM.FileStorage.Catalog:
/// a delete keeps the bytes so a restore is instant, but not forever. Their rows go and their bytes are deleted (or
/// queued for the storage-cleanup job if that fails).
/// </summary>
[ScheduledJob(JobName, Cron = "0 0 * * * ?")]
public sealed class RetentionJob(StoredFileCatalogRuns<StoredFile, StorageCleanup> runs, IOptions<RetentionOptions> options)
    : IScheduledJob
{
    public const string JobName = "retention";

    public Task<JobExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken) =>
        RunAsync(null, cancellationToken);

    /// <summary>One run: files deleted too long ago, optionally only those <paramref name="within"/> matches.</summary>
    public async Task<JobExecutionResult> RunAsync(Expression<Func<StoredFile, bool>>? within, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.DeletedFileDays <= 0)
            return JobExecutionResult.Success(0, "Retention is off: deleted files are kept.");

        var run = await runs.PurgeDeletedAsync(
            DateTime.UtcNow.AddDays(-settings.DeletedFileDays), settings.BatchSize, within, cancellationToken);
        return StorageCleanupJob.ToJobResult(run, $"Purged {run.Processed} file(s) deleted more than {settings.DeletedFileDays} days ago.");
    }
}
