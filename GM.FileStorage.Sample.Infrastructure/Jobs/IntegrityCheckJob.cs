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

/// <summary>The integrity job's settings (<c>Integrity</c> in the worker's appsettings.json).</summary>
public class IntegrityOptions
{
    /// <summary>Files checked per run (every 5 minutes), the least recently checked first.</summary>
    public int BatchSize { get; set; } = 20;
}

/// <summary>
/// Reads stored files back and checks them against the catalog, through GM.FileStorage.Catalog's
/// <c>StoredFileVerifier</c>: the bytes exist, have the recorded size, and hash to the recorded checksum (decrypted first
/// when encryption at rest is on). A file stored without a checksum gets one recorded. With malware scanning on, the
/// bytes are rescanned too. What's found is kept on the file (<see cref="StoredFile.VerifiedAtUtc"/>,
/// <see cref="StoredFile.IntegrityProblem"/>), so problems show in the files list and details. Deleted files are
/// checked too: their bytes are kept for a restore.
/// </summary>
[ScheduledJob(JobName, Cron = "0 */5 * * * ?")]
public sealed class IntegrityCheckJob(StoredFileCatalogRuns<StoredFile, StorageCleanup> runs, IOptions<IntegrityOptions> options)
    : IScheduledJob
{
    public const string JobName = "integrity-check";

    public Task<JobExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken) =>
        RunAsync(null, cancellationToken);

    /// <summary>One run: the least recently checked files, optionally only those <paramref name="within"/> matches.</summary>
    public async Task<JobExecutionResult> RunAsync(Expression<Func<StoredFile, bool>>? within, CancellationToken cancellationToken)
    {
        var run = await runs.VerifyAsync(options.Value.BatchSize, within, cancellationToken);
        return StorageCleanupJob.ToJobResult(run, $"Checked {run.Processed} stored file(s); {run.Problems} with a problem.");
    }
}
