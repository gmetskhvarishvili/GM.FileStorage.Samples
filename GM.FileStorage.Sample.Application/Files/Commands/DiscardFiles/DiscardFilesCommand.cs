using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.DiscardFiles;

/// <summary>
/// Undoes uploads the caller was never told about: files stored while streaming a request that then turned out to
/// be invalid (several files sent to the single upload, too many in a batch). Unlike <c>DeleteFileCommand</c>,
/// nothing is kept — the rows go, and the bytes are deleted. Not exposed over HTTP.
/// </summary>
public class DiscardFilesCommand : IRequest
{
    public IReadOnlyCollection<Guid> Ids { get; set; } = [];
}

/// <remarks>
/// In one transaction the file and metadata rows are removed and each storage key is queued for cleanup, due now;
/// then the bytes are deleted. A failed delete stays queued for the storage-cleanup job, so nothing is left behind.
/// </remarks>
public class DiscardFilesCommandHandler(IUnitOfWork unitOfWork, IFileStorageService storage)
    : IRequestHandler<DiscardFilesCommand>
{
    public async Task Handle(DiscardFilesCommand request, CancellationToken cancellationToken)
    {
        if (request.Ids.Count == 0)
            return;

        var ids = request.Ids.ToList();
        var files = await unitOfWork.StoredFileRepository.FindAsync(x => ids.Contains(x.Id), false, null, cancellationToken);
        var metadata = await unitOfWork.StoredFileMetadataRepository
            .FindAsync(x => ids.Contains(x.StoredFileId), false, null, cancellationToken);

        var cleanups = new List<StorageCleanup>(files.Count);
        foreach (var file in files)
        {
            var cleanup = StorageCleanup.Create(file.StorageKey, DateTime.UtcNow);
            await unitOfWork.StorageCleanupRepository.AddAsync(cleanup, cancellationToken);
            cleanups.Add(cleanup);
        }

        unitOfWork.StoredFileMetadataRepository.RemoveRange(metadata);
        unitOfWork.StoredFileRepository.RemoveRange(files);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var cleanup in cleanups)
        {
            // A failure leaves its changes tracked on this unit of work; stop there and let the job do the rest.
            if (!await StorageCleanups.TryCompleteAsync(unitOfWork, storage, cleanup, CancellationToken.None))
                break;
        }
    }
}
