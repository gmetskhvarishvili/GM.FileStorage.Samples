using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Domain.SeedWork;
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
/// In one transaction the file and metadata rows are removed and each storage key is queued for cleanup, due now;
/// then the bytes are deleted. A failed delete stays queued for the storage-cleanup job, so nothing is left behind.
/// </remarks>
public class DiscardFilesCommandHandler(IUnitOfWork unitOfWork, IFileStorageService storage)
    : IRequestHandler<DiscardFilesCommand>
{
    public Task Handle(DiscardFilesCommand request, CancellationToken cancellationToken) =>
        StoredFilePurge.PurgeAsync(unitOfWork, storage, request.Ids, cancellationToken);
}
