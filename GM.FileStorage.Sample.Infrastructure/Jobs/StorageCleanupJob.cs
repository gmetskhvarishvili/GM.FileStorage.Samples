using GM.FileStorage.Catalog;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.Scheduling;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Infrastructure.Jobs;

/// <summary>
/// Deletes the storage objects nothing points at — the bytes of uploads that were stored but never recorded
/// (see <c>StorageCleanup</c>), through GM.FileStorage.Catalog. A deleted file keeps its bytes (it can be restored), so
/// they are never queued. A failed entry backs off and is retried on a later run; an entry whose storage key a file
/// still uses is dropped without deleting anything. Runs every minute, guarded by a distributed lock (by
/// GM.Scheduling), so only one instance cleans at a time.
/// </summary>
[ScheduledJob(JobName, Cron = "0 * * * * ?")]
public sealed class StorageCleanupJob(StoredFileCatalogRuns<StoredFile, StorageCleanup> runs) : IScheduledJob
{
    public const string JobName = "storage-cleanup";

    /// <summary>Most entries one run carries out; the rest wait for the next minute.</summary>
    public const int BatchSize = 100;

    public async Task<JobExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        var run = await runs.CleanUpDueAsync(BatchSize, cancellationToken);
        return ToJobResult(run, run.Succeeded
            ? $"Deleted {run.Processed} unreferenced storage object(s)."
            : $"Deleted {run.Processed}; {run.Failures.Count} will be retried.");
    }

    internal static JobExecutionResult ToJobResult(CatalogRunResult run, string message) =>
        run.Succeeded
            ? JobExecutionResult.Success(run.Processed, message)
            : JobExecutionResult.Partial(run.Processed, run.Failures, message);
}
