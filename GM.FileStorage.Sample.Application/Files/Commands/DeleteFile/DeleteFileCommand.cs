using FluentValidation;
using GM.Exceptions;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.DeleteFile;

/// <summary>
/// Deletes a file from the catalog only: its row is soft-removed (hidden from list, details, download and presign)
/// and its bytes stay in storage, so <c>RestoreFileCommand</c> can bring it back exactly as it was. Uploading to
/// the same key again also brings the row back, with the new content.
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

public class DeleteFileCommandHandler(IUnitOfWork unitOfWork)
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

        // Only the row changes; the bytes are kept for a restore.
        entity.SoftRemove();

        unitOfWork.StoredFileRepository.Update(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
