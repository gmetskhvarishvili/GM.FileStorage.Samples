using FluentValidation;
using GM.EntityFramework.Domain.Common;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.DeleteFile;

/// <summary>
/// Deletes a file from the catalog only: its row is soft-removed (hidden from list, details, download and presign)
/// and its bytes stay in storage, so <c>RestoreFileCommand</c> can bring it back exactly as it was.
/// </summary>
public class DeleteFileCommand : IRequest
{
    public Guid Id { get; set; }
}

public class DeleteFileCommandValidator : AbstractValidator<DeleteFileCommand>
{
    public DeleteFileCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DeleteFileCommandHandler(IUnitOfWork unitOfWork, ICurrentActor actor)
    : IRequestHandler<DeleteFileCommand>
{
    public async Task Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var tenantId = actor.TenantOrShared();
        var entity = await unitOfWork.StoredFileRepository
            .FirstOrDefaultAsync(x => x.Id == request.Id
                                      && x.Metadata.Any(m => m.Name == StoredFileMetadataNames.TenantId && m.Value == tenantId)
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
                StringResource.Id,
                request.Id);
        }

        // Only the row changes; the bytes are kept for a restore.
        entity.SoftRemove();

        unitOfWork.StoredFileRepository.Update(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
