using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
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
/// Purges files deleted longer ago than <see cref="RetentionOptions.DeletedFileDays"/>: a delete keeps the bytes so a
/// restore is instant, but not forever. Their rows go and their bytes are deleted (or queued for the storage-cleanup
/// job if that fails).
/// </summary>
[ScheduledJob(JobName, Cron = "0 0 * * * ?")]
public sealed class RetentionJob(IServiceScopeFactory scopeFactory, IOptions<RetentionOptions> options) : IScheduledJob
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

        await using var scope = scopeFactory.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        var cutoff = DateTime.UtcNow.AddDays(-settings.DeletedFileDays);
        var files = unitOfWork.StoredFileRepository.Query(asNoTracking: true);
        if (within is not null)
            files = files.Where(within);
        List<Guid> expired = files
            .Where(x => x.IsDeleted && x.DeletedAtUtc != null && x.DeletedAtUtc < cutoff)
            .OrderBy(x => x.DeletedAtUtc)
            .Take(Math.Max(1, settings.BatchSize))
            .Select(x => x.Id)
            .ToList();

        var purged = await StoredFilePurge.PurgeAsync(unitOfWork, storage, expired, cancellationToken);
        return JobExecutionResult.Success(purged, $"Purged {purged} file(s) deleted more than {settings.DeletedFileDays} days ago.");
    }
}
