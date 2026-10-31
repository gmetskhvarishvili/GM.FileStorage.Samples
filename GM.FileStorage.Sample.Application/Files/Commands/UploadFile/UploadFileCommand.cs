using FluentValidation;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.UploadFile;

/// <summary>
/// Stores a file through <see cref="IFileStorageService"/> and records it in the catalog: a
/// <see cref="StoredFile"/> row plus one <see cref="StoredFileMetadata"/> row per metadata pair, so files can be
/// listed by tenant or user. Uploading to an existing <see cref="Key"/> (even a deleted one) replaces and restores
/// its row instead of adding a second.
/// </summary>
public class UploadFileCommand : IRequest<UploadedFileDto>
{
    /// <summary>The bytes, streamed straight into storage (never buffered whole).</summary>
    public Stream? Content { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }

    /// <summary>Tenant the file belongs to; defaults to <c>public</c>.</summary>
    public string? TenantId { get; set; }

    /// <summary>User the file belongs to; defaults to <c>anon</c>.</summary>
    public string? UserId { get; set; }

    /// <summary>An explicit storage key. Null lets the key strategy generate a tenant/user-scoped one.</summary>
    public string? Key { get; set; }
}

public class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    public UploadFileCommandValidator()
    {
        RuleFor(x => x.Content).NotNull();
        RuleFor(x => x.FileName).NotNull().NotEmpty().MaximumLength(StoredFile.FileNameMaxLength);
        RuleFor(x => x.ContentType).NotNull().NotEmpty().MaximumLength(StoredFile.ContentTypeMaxLength);
        RuleFor(x => x.TenantId).MaximumLength(StoredFileMetadata.ValueMaxLength);
        RuleFor(x => x.UserId).MaximumLength(StoredFileMetadata.ValueMaxLength);
        RuleFor(x => x.Key).MaximumLength(StoredFile.KeyMaxLength);
    }
}

public class UploadFileCommandHandler(IUnitOfWork unitOfWork, IFileStorageService storage)
    : IRequestHandler<UploadFileCommand, UploadedFileDto>
{
    public async Task<UploadedFileDto> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        // Tenant and user travel as upload metadata: TenantKeyStrategy shapes the key from them, and the catalog
        // keeps them as searchable rows.
        var metadata = new Dictionary<string, string>
        {
            [StoredFileMetadataNames.TenantId] = string.IsNullOrWhiteSpace(request.TenantId) ? "public" : request.TenantId,
            [StoredFileMetadataNames.UserId] = string.IsNullOrWhiteSpace(request.UserId) ? "anon" : request.UserId,
        };

        var stored = await storage.UploadAsync(
            new FileUploadRequest(request.Content!, request.FileName!, request.ContentType!)
            {
                Key = request.Key,
                Visibility = FileVisibility.Private,
                Metadata = metadata,
            },
            cancellationToken);

        try
        {
            var uploadedAtUtc = stored.LastModifiedUtc?.UtcDateTime ?? DateTime.UtcNow;
            var isPublic = stored.Visibility == FileVisibility.Public;

            // One row per key, deleted or not (the key is unique), so look it up regardless of visibility.
            var entity = await unitOfWork.StoredFileRepository
                .FirstOrDefaultAsync(x => x.Key == stored.Key, true, null, cancellationToken);

            if (entity == null)
            {
                entity = StoredFile.Create(stored.Key, stored.FileName, stored.ContentType, stored.Size,
                    stored.Checksum, isPublic, uploadedAtUtc);
                await unitOfWork.StoredFileRepository.AddAsync(entity, cancellationToken);
            }
            else
            {
                // New content at the same key: restores the row if it had been deleted.
                entity.Replace(stored.FileName, stored.ContentType, stored.Size, stored.Checksum, isPublic, uploadedAtUtc);
                unitOfWork.StoredFileRepository.Update(entity);
            }

            await SyncMetadataAsync(entity.Id, metadata, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new UploadedFileDto
            {
                Id = entity.Id,
                Key = entity.Key,
                FileName = entity.FileName,
                ContentType = entity.ContentType,
                Size = entity.Size,
                Checksum = entity.Checksum,
            };
        }
        catch
        {
            // The catalog row was not saved, so don't leave unreferenced bytes behind. (When this replaced an
            // existing key, its previous content was already overwritten by the upload.)
            await storage.DeleteAsync(stored.Key, CancellationToken.None);
            throw;
        }
    }

    // Makes the file's metadata rows match the upload: changes or restores the rows that exist (a name is unique
    // per file, deleted or not), adds the new names, and soft-removes the names the upload no longer carries.
    private async Task SyncMetadataAsync(
        Guid storedFileId, Dictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        var existing = await unitOfWork.StoredFileMetadataRepository
            .FindAsync(x => x.StoredFileId == storedFileId, true, null, cancellationToken);

        foreach (var (name, value) in metadata)
        {
            var row = existing.FirstOrDefault(x => x.Name == name);
            if (row == null)
            {
                await unitOfWork.StoredFileMetadataRepository.AddAsync(
                    StoredFileMetadata.Create(storedFileId, name, value), cancellationToken);
                continue;
            }

            if (row.IsDeleted)
                row.RestoreAll();

            row.ChangeValue(value);
            unitOfWork.StoredFileMetadataRepository.Update(row);
        }

        foreach (var row in existing.Where(x => !x.IsDeleted && !metadata.ContainsKey(x.Name)))
        {
            row.SoftRemove();
            unitOfWork.StoredFileMetadataRepository.Update(row);
        }
    }
}

/// <summary>The catalog record of a file that was just stored.</summary>
public class UploadedFileDto
{
    public Guid Id { get; set; }
    public string? Key { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Checksum { get; set; }
}
