using GM.FileStorage.Encryption;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
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
/// Reads stored files back and checks them against the catalog: the bytes exist, have the recorded size, and hash to
/// the recorded checksum (decrypted first when encryption at rest is on, as the checksum is the plaintext's). A file
/// stored without a checksum (recorded before checksums were kept) gets one recorded. What's
/// found is kept on the file (<see cref="StoredFile.VerifiedAtUtc"/>, <see cref="StoredFile.IntegrityProblem"/>),
/// so problems show in the files list and details. Deleted files are checked too: their bytes are kept for a restore.
/// </summary>
[ScheduledJob(JobName, Cron = "0 */5 * * * ?")]
public sealed class IntegrityCheckJob(
    IServiceScopeFactory scopeFactory, IOptions<IntegrityOptions> options, ILogger<IntegrityCheckJob> logger) : IScheduledJob
{
    public const string JobName = "integrity-check";

    public Task<JobExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken) =>
        RunAsync(null, cancellationToken);

    /// <summary>One run: the least recently checked files, optionally only those <paramref name="within"/> matches.</summary>
    public async Task<JobExecutionResult> RunAsync(Expression<Func<StoredFile, bool>>? within, CancellationToken cancellationToken)
    {
        List<Guid> next;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var files = unitOfWork.StoredFileRepository.Query(asNoTracking: true);
            if (within is not null)
                files = files.Where(within);
            next = files
                .OrderBy(x => x.VerifiedAtUtc.HasValue)  // never checked first
                .ThenBy(x => x.VerifiedAtUtc)
                .Take(Math.Max(1, options.Value.BatchSize))
                .Select(x => x.Id)
                .ToList();
        }

        int checkedFiles = 0, problems = 0;
        var failures = new List<string>();
        foreach (var id in next)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

            var file = await unitOfWork.StoredFileRepository.FirstOrDefaultAsync(x => x.Id == id, false, null, cancellationToken);
            if (file == null)
                continue; // purged since the query

            string? problem;
            try
            {
                problem = await CheckAsync(storage, file, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The storage didn't answer (not a verdict on the bytes): try again next run.
                logger.LogWarning(ex, "Checking stored file {FileId} failed; it will be retried.", file.Id);
                failures.Add(file.Id.ToString());
                continue;
            }

            if (problem is not null)
            {
                problems++;
                logger.LogWarning("Stored file {FileId} ({StorageKey}) failed its integrity check: {Problem}", file.Id, file.StorageKey, problem);
            }

            file.RecordVerification(DateTime.UtcNow, problem);
            unitOfWork.StoredFileRepository.Update(file);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            checkedFiles++;
        }

        var message = $"Checked {checkedFiles} stored file(s); {problems} with a problem.";
        return failures.Count == 0
            ? JobExecutionResult.Success(checkedFiles, message)
            : JobExecutionResult.Partial(checkedFiles, failures, message);
    }

    // Null when the stored bytes match the record; otherwise what's wrong. A missing checksum is recorded.
    private static async Task<string?> CheckAsync(IFileStorageService storage, StoredFile file, CancellationToken cancellationToken)
    {
        FileDownloadResult download;
        try
        {
            download = await storage.DownloadAsync(file.StorageKey, cancellationToken);
        }
        catch (FileStorageObjectNotFoundException)
        {
            return "The stored bytes are missing.";
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        await using (download)
        {
            var buffer = new byte[81920];
            try
            {
                int read;
                while ((read = await download.Content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    size += read;
                }
            }
            catch (FileDecryptionException)
            {
                return "The stored bytes fail decryption: they were altered or truncated.";
            }
        }

        if (size != file.Size)
            return $"The stored bytes are {size} bytes, not the {file.Size} recorded.";

        var checksum = Convert.ToHexStringLower(sha.GetHashAndReset());
        if (file.Checksum is null)
        {
            file.RecordChecksum(checksum);
            return null;
        }

        return string.Equals(file.Checksum, checksum, StringComparison.OrdinalIgnoreCase)
            ? null
            : "The stored bytes don't match the recorded checksum.";
    }
}
