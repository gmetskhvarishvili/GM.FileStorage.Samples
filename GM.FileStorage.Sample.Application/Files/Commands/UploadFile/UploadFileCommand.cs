using FluentValidation;
using GM.Exceptions;
using GM.EntityFramework.Domain.Exceptions;
using GM.FileStorage.Sample.Application.Common;
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

/// <remarks>
/// Storage and the catalog can't share a transaction, so the handler orders its steps so that every failure
/// leaves at worst unreferenced bytes, and those are always queued for deletion (<see cref="StorageCleanup"/>):
/// <list type="number">
/// <item>Queue the new storage key for cleanup (due after a grace period) and save — before any bytes exist.</item>
/// <item>Upload under that key. Each upload gets its own versioned key, so nothing the catalog points at is ever
/// overwritten.</item>
/// <item>In one transaction: point the file row at the new key, sync its metadata, drop the pending entry, and
/// queue the previous version's key. If this fails, nothing the catalog points at changed; the new bytes are
/// deleted (or left to the job).</item>
/// <item>Delete the previous version's bytes; a failure leaves them queued for the job.</item>
/// </list>
/// Two uploads racing to the same key can't both win: the row's storage key is a concurrency token (and the key
/// is unique), so the loser's save fails and it gets a 400 asking it to retry.
/// </remarks>
public class UploadFileCommandHandler(IUnitOfWork unitOfWork, IFileStorageService storage, IFileKeyStrategy keyStrategy)
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

        var key = string.IsNullOrWhiteSpace(request.Key)
            ? keyStrategy.GenerateKey(new FileKeyContext(request.FileName!, metadata))
            : request.Key;
        var storageKey = StoredFile.NewStorageKey(key);

        // 1. Queue the new storage key before any bytes exist, so a crash after the upload can't strand them.
        var pending = StorageCleanup.Create(storageKey, DateTime.UtcNow.Add(StorageCleanup.PendingUploadGracePeriod));
        await unitOfWork.StorageCleanupRepository.AddAsync(pending, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // 2. Store the bytes under their own versioned key. FileStorage:MaxFileSize / AllowedContentTypes /
        // VerifyContentType are enforced inside UploadAsync before anything is kept; a rejection is the caller's
        // mistake, so it surfaces as a 400 with the reason. Any other failure leaves the pending entry, and the job
        // removes whatever was partly written.
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

            // One row per key, deleted or not (the key is unique). Tracked, so the save checks the row still has
            // the storage key read here (its concurrency token).
            var existing = await unitOfWork.StoredFileRepository
                .FirstOrDefaultAsync(x => x.Key == key, false, null, cancellationToken);

            if (existing == null)
            {
                entity = StoredFile.Create(key, storageKey, stored.FileName, stored.ContentType, stored.Size,
                    stored.Checksum, isPublic, uploadedAtUtc);
                await unitOfWork.StoredFileRepository.AddAsync(entity, cancellationToken);
            }
            else
            {
                // New content at the same key (restores the row if it had been deleted); the old bytes stay put
                // until this is saved, then go.
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
            // ConcurrencyException) or on the unique key (two first uploads), and another upload is now the file.
            if (ex is ConcurrencyException or Microsoft.EntityFrameworkCore.DbUpdateException
                && await unitOfWork.StoredFileRepository.ExistsAsync(
                    x => x.Key == key && x.StorageKey != storageKey && !x.IsDeleted, CancellationToken.None))
            {
                throw new BadRequestException(
                    $"Another upload to '{key}' finished first, so this one wasn't saved. Retry the upload.");
            }

            throw;
        }

        // 4. The previous version is unreferenced now; delete it (a failure leaves it queued for the job).
        if (previousVersion != null)
            await StorageCleanups.TryCompleteAsync(unitOfWork, storage, previousVersion, CancellationToken.None);

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
