using GM.FileStorage.Catalog;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.Mediator.Contracts;
using System;
using System.Collections.Generic;
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
/// GM.FileStorage.Catalog's purge: in one transaction the rows are removed and each storage key is queued for cleanup,
/// due now; then the bytes are deleted. A failed delete stays queued for the storage-cleanup job.
/// </remarks>
public class DiscardFilesCommandHandler(StoredFileCatalog<StoredFile, StorageCleanup> catalog)
    : IRequestHandler<DiscardFilesCommand>
{
    public Task Handle(DiscardFilesCommand request, CancellationToken cancellationToken) =>
        catalog.PurgeAsync(request.Ids, cancellationToken);
}
