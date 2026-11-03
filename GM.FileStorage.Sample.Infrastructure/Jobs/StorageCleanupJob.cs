using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Infrastructure.Jobs;

/// <summary>
/// Deletes the storage objects nothing points at — the bytes of uploads that were stored but never recorded
/// (see <c>StorageCleanup</c>). A deleted file keeps its bytes (it can be restored), so
/// they are never queued. Each due entry is carried out in its own DI scope, so one failure doesn't affect the rest;
/// a failed entry backs off and is retried on a later run. An entry whose storage key any file row still uses is
/// dropped without deleting anything. Runs every minute, guarded by a distributed lock (by GM.Scheduling), so only
/// one instance cleans at a time.
/// </summary>
[ScheduledJob(JobName, Cron = "0 * * * * ?")]
public sealed class StorageCleanupJob(IServiceScopeFactory scopeFactory) : IScheduledJob
{
    public const string JobName = "storage-cleanup";

    /// <summary>Most entries one run carries out; the rest wait for the next minute.</summary>
    public const int BatchSize = 100;

    public async Task<JobExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        List<Guid> due;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var now = DateTime.UtcNow;
            due = unitOfWork.StorageCleanupRepository.Query(asNoTracking: true)
                .Where(x => x.DueAtUtc <= now)
                .OrderBy(x => x.DueAtUtc)
                .Take(BatchSize)
                .Select(x => x.Id)
                .ToList();
        }

        var cleaned = 0;
        var failures = new List<string>();
        foreach (var id in due)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

            var entry = await unitOfWork.StorageCleanupRepository.FirstOrDefaultAsync(x => x.Id == id, false, null, cancellationToken);
            if (entry == null)
                continue; // taken care of since the query (e.g. by the request that queued it)

            // A file still uses these bytes — live, or deleted and so restorable — (it can't normally happen: every
            // upload has its own key); keep them.
            if (await unitOfWork.StoredFileRepository.ExistsAsync(x => x.StorageKey == entry.StorageKey, cancellationToken))
            {
                unitOfWork.StorageCleanupRepository.Remove(entry);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                continue;
            }

            if (await StorageCleanups.TryCompleteAsync(unitOfWork, storage, entry, cancellationToken))
                cleaned++;
            else
                failures.Add(entry.StorageKey);
        }

        return failures.Count == 0
            ? JobExecutionResult.Success(cleaned, $"Deleted {cleaned} unreferenced storage object(s).")
            : JobExecutionResult.Partial(cleaned, failures, $"Deleted {cleaned}; {failures.Count} will be retried.");
    }
}
