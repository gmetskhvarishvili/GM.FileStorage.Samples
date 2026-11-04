using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Common;

/// <summary>
/// Removes files for good: unlike a delete, nothing is kept to restore. Used for files a refused request stored
/// (<c>DiscardFilesCommand</c>) and for files deleted longer ago than retention allows (the retention job).
/// </summary>
public static class StoredFilePurge
{
    /// <summary>
    /// In one transaction the file and metadata rows are removed and each storage key is queued for cleanup, due now;
    /// then the bytes are deleted. A failed delete stays queued for the storage-cleanup job, so nothing is left behind.
    /// Returns how many files were purged.
    /// </summary>
    public static async Task<int> PurgeAsync(
        IUnitOfWork unitOfWork, IFileStorageService storage, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return 0;

        var idList = ids.ToList();
        var files = await unitOfWork.StoredFileRepository.FindAsync(x => idList.Contains(x.Id), false, null, cancellationToken);

        var cleanups = new List<StorageCleanup>(files.Count);
        foreach (var file in files)
        {
            var cleanup = StorageCleanup.Create(file.StorageKey, DateTime.UtcNow);
            await unitOfWork.StorageCleanupRepository.AddAsync(cleanup, cancellationToken);
            cleanups.Add(cleanup);
        }

        unitOfWork.StoredFileRepository.RemoveRange(files);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var cleanup in cleanups)
        {
            // A failure leaves its changes tracked on this unit of work; stop there and let the job do the rest.
            if (!await StorageCleanups.TryCompleteAsync(unitOfWork, storage, cleanup, CancellationToken.None))
                break;
        }

        return files.Count;
    }
}
