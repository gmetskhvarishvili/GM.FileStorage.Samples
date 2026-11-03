using FluentValidation;
using GM.Exceptions;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.DeleteFile;

/// <summary>
/// Deletes a file's bytes from storage and soft-removes its catalog row, which stays behind as an audit trail.
/// Uploading to the same key again restores the row.
/// </summary>
public class DeleteFileCommand : IRequest
{
    public string? Key { get; set; }
}

public class DeleteFileCommandValidator : AbstractValidator<DeleteFileCommand>
{
    public DeleteFileCommandValidator()
    {
        RuleFor(x => x.Key).NotNull().NotEmpty();
    }
}

public class DeleteFileCommandHandler(IUnitOfWork unitOfWork, IFileStorageService storage)
    : IRequestHandler<DeleteFileCommand>
{
    public async Task Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var entity = await unitOfWork.StoredFileRepository
            .FirstOrDefaultAsync(x => x.Key == request.Key
                                      && x.IsActive
                                      && !x.IsDeleted
                                      && !x.IsHidden,
                true,
                null,
                cancellationToken);

        if (entity == null)
        {
            throw new NotFoundException(
                StringResource.StoredFile,
                StringResource.Key,
                request.Key!);
        }

        // The row goes first, and its bytes are queued for deletion in the same transaction: the catalog never
        // points at missing content, and if deleting the bytes fails below, the storage-cleanup job retries.
        entity.SoftRemove();
        var cleanup = StorageCleanup.Create(entity.StorageKey, DateTime.UtcNow);

        unitOfWork.StoredFileRepository.Update(entity);
        await unitOfWork.StorageCleanupRepository.AddAsync(cleanup, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await StorageCleanups.TryCompleteAsync(unitOfWork, storage, cleanup, CancellationToken.None);
    }
}
