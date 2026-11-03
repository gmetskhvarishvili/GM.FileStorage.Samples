using FluentValidation;
using GM.Exceptions;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.RestoreFile;

/// <summary>
/// Brings a deleted file back: its row is restored (active and visible again) with the content, metadata and
/// storage key it had when it was deleted. Delete keeps the bytes, so nothing has to be re-uploaded.
/// </summary>
public class RestoreFileCommand : IRequest
{
    public Guid Id { get; set; }
}

public class RestoreFileCommandValidator : AbstractValidator<RestoreFileCommand>
{
    public RestoreFileCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class RestoreFileCommandHandler(IUnitOfWork unitOfWork, IFileStorageService storage)
    : IRequestHandler<RestoreFileCommand>
{
    public async Task Handle(RestoreFileCommand request, CancellationToken cancellationToken)
    {
        var entity = await unitOfWork.StoredFileRepository
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.IsDeleted, true, null, cancellationToken);

        // Restoring only makes sense when the bytes are still there (they could have been removed out of band).
        if (entity == null || !await storage.ExistsAsync(entity.StorageKey, cancellationToken))
        {
            throw new NotFoundException(
                StringResource.StoredFile,
                StringResource.Id,
                request.Id);
        }

        entity.RestoreAll();

        unitOfWork.StoredFileRepository.Update(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
