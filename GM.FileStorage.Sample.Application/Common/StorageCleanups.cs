using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Common;

/// <summary>
/// Carries out one <see cref="StorageCleanup"/>: claims it, deletes its bytes, then removes it. Used by the
/// requests that orphan bytes (straight after they save) and by the storage-cleanup job (for whatever they left).
/// </summary>
public static class StorageCleanups
{
    /// <summary>
    /// Returns whether the bytes were deleted and the entry removed. Never throws for a storage or database
    /// failure: the entry stays queued (claimed with a backoff) and the job tries again. Run it on a unit of work
    /// whose only pending change is this entry, since a failed save leaves its changes tracked.
    /// </summary>
    public static async Task<bool> TryCompleteAsync(
        IUnitOfWork unitOfWork, IFileStorageService storage, StorageCleanup entry, CancellationToken cancellationToken)
    {
        try
        {
            entry.Claim(DateTime.UtcNow);
            unitOfWork.StorageCleanupRepository.Update(entry);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await storage.DeleteAsync(entry.StorageKey, cancellationToken);

            unitOfWork.StorageCleanupRepository.Remove(entry);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
#pragma warning disable CA1031 // Best effort by design: a failure leaves the entry for the job.
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
