using FluentValidation;
using GM.Exceptions;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
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

        entity.SoftRemove();

        unitOfWork.StoredFileRepository.Update(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Bytes go after the row: if this fails the catalog already says "deleted" and only an orphaned object is
        // left behind, never a row pointing at missing content.
        await storage.DeleteAsync(entity.Key, cancellationToken);
    }
}
