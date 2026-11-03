using FluentValidation;
using GM.Exceptions;
using GM.EntityFramework.Domain.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
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
/// listed by tenant or user. Without <see cref="Id"/> it's a new file, identified by a new GUID; with one, it
/// replaces that file's content (restoring it if it was deleted).
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

    /// <summary>The file whose content this replaces; null uploads a new file.</summary>
    public Guid? Id { get; set; }
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
        RuleFor(x => x.Id).NotEqual(Guid.Empty);
    }
}

/// <remarks>
/// A file has one identifier, a GUID (its id, which is also its key). Where its bytes live is separate: every
/// upload gets a fresh storage key from FileStorage:KeyTemplate (e.g. <c>documents/9f/9f1c….pdf</c>), so a
/// replacement never overwrites content the catalog still points at.
/// <para>
/// Storage and the catalog can't share a transaction, so the handler orders its steps so that every failure
/// leaves at worst unreferenced bytes, and those are always queued for deletion (<see cref="StorageCleanup"/>):
/// </para>
/// <list type="number">
/// <item>Queue the new storage key for cleanup (due after a grace period) and save — before any bytes exist.</item>
/// <item>Upload under that key.</item>
/// <item>In one transaction: create the file row or point it at the new key, sync its metadata, drop the pending
/// entry, and queue the previous version's key. If this fails, nothing the catalog points at changed; the new
/// bytes are deleted (or left to the job).</item>
/// <item>Delete the previous version's bytes; a failure leaves them queued for the job.</item>
/// </list>
/// Two uploads replacing the same file at once can't both win: the row's storage key is a concurrency token, so
/// the loser's save fails and it gets a 400 asking it to retry.
/// </remarks>
public class UploadFileCommandHandler(IUnitOfWork unitOfWork, IFileStorageService storage, IFileKeyStrategy keyStrategy)
    : IRequestHandler<UploadFileCommand, UploadedFileDto>
{
    public async Task<UploadedFileDto> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        // Tenant and user travel as upload metadata, kept by the catalog as searchable rows.
        var metadata = new Dictionary<string, string>
        {
            [StoredFileMetadataNames.TenantId] = string.IsNullOrWhiteSpace(request.TenantId) ? "public" : request.TenantId,
            [StoredFileMetadataNames.UserId] = string.IsNullOrWhiteSpace(request.UserId) ? "anon" : request.UserId,
        };

        // A replacement targets an existing file (deleted or not), checked before any bytes are stored. Tracked, so
        // the save checks the row still has the storage key read here (its concurrency token).
        StoredFile? existing = null;
        if (request.Id is { } id)
        {
            existing = await unitOfWork.StoredFileRepository
                .FirstOrDefaultAsync(x => x.Id == id, false, null, cancellationToken)
                ?? throw new NotFoundException(StringResource.StoredFile, StringResource.Id, id);
        }

        // Where these bytes go: a fresh path from FileStorage:KeyTemplate. It's needed before the upload (it's queued
        // first), so it's built from the declared content type; with FileStorage:VerifyContentType on, a declaration
        // the bytes don't match is rejected by the upload, so the {category} folder can't lie.
        var storageKey = keyStrategy.GenerateKey(new FileKeyContext(request.FileName!, metadata) { ContentType = request.ContentType });

        // 1. Queue the new storage key before any bytes exist, so a crash after the upload can't strand them.
        var pending = StorageCleanup.Create(storageKey, DateTime.UtcNow.Add(StorageCleanup.PendingUploadGracePeriod));
        await unitOfWork.StorageCleanupRepository.AddAsync(pending, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // 2. Store the bytes. FileStorage:MaxFileSize / AllowedContentTypes / VerifyContentType are enforced inside
        // UploadAsync before anything is kept; a rejection is the caller's mistake, so it surfaces as a 400 with the
        // reason. Any other failure leaves the pending entry, and the job removes whatever was partly written.
        FileMetadata stored;
        try
        {
            stored = await storage.UploadAsync(
                new FileUploadRequest(request.Content!, request.FileName!, request.ContentType!)
                {
                    Key = storageKey,
                    Visibility = FileVisibility.Private,
                    Metadata = metadata,
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is FileTooLargeException or FileContentTypeNotAllowedException or FileContentTypeMismatchException)
        {
            await StorageCleanups.TryCompleteAsync(unitOfWork, storage, pending, CancellationToken.None);
            throw new BadRequestException(ex.Message);
        }

        // 3. Record it, in one transaction.
        StoredFile entity;
        StorageCleanup? previousVersion = null;
        try
        {
            var uploadedAtUtc = stored.LastModifiedUtc?.UtcDateTime ?? DateTime.UtcNow;
            var isPublic = stored.Visibility == FileVisibility.Public;

            if (existing == null)
            {
                entity = StoredFile.Create(Guid.NewGuid(), storageKey, stored.FileName, stored.ContentType, stored.Size,
                    stored.Checksum, isPublic, uploadedAtUtc);
                await unitOfWork.StoredFileRepository.AddAsync(entity, cancellationToken);
            }
            else
            {
                // New content for the same file (restores it if it had been deleted); the old bytes stay put until
                // this is saved, then go.
                entity = existing;
                var previousStorageKey = entity.Replace(storageKey, stored.FileName, stored.ContentType, stored.Size,
                    stored.Checksum, isPublic, uploadedAtUtc);
                previousVersion = StorageCleanup.Create(previousStorageKey, DateTime.UtcNow);
                await unitOfWork.StorageCleanupRepository.AddAsync(previousVersion, cancellationToken);
            }

            await SyncMetadataAsync(entity.Id, metadata, cancellationToken);

            // The bytes are referenced now, so they're no longer the pending upload's to clean up. If the job has
            // already claimed the entry (an upload slower than the grace period), this save fails instead.
            unitOfWork.StorageCleanupRepository.Remove(pending);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Not recorded: the catalog still points at whatever it did before, and our bytes are unreferenced.
            // Delete them now; the pending entry is still queued, so the job retries if this fails.
            try { await storage.DeleteAsync(storageKey, CancellationToken.None); }
#pragma warning disable CA1031 // Best effort: the queued entry covers it.
            catch (Exception) { }
#pragma warning restore CA1031

            // A lost race: the save failed on the row's storage key (GM's unit of work reports that as a
            // ConcurrencyException) because another replacement of the same file finished first.
            if (ex is ConcurrencyException && existing != null)
            {
                throw new BadRequestException(
                    $"Another upload to file '{existing.Id}' finished first, so this one wasn't saved. Retry the upload.");
            }

            throw;
        }

        // 4. The previous version is unreferenced now; delete it (a failure leaves it queued for the job).
        if (previousVersion != null)
            await StorageCleanups.TryCompleteAsync(unitOfWork, storage, previousVersion, CancellationToken.None);

        return new UploadedFileDto
        {
            Id = entity.Id,
            StorageKey = StorageKeys.Name(entity.StorageKey),
            Folder = StorageKeys.Folder(entity.StorageKey),
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            Size = entity.Size,
            Checksum = entity.Checksum,
        };
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
    /// <summary>The file's identifier (a GUID): use it for every other call.</summary>
    public Guid Id { get; set; }

    /// <summary>The stored object's name, e.g. <c>9f1c….pdf</c>.</summary>
    public string? StorageKey { get; set; }

    /// <summary>The folder it's stored in, e.g. <c>documents/9f/</c>.</summary>
    public string? Folder { get; set; }

    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Checksum { get; set; }
}
