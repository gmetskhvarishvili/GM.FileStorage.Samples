using FluentValidation;
using GM.EntityFramework.Domain.Common;
using GM.Exceptions;
using GM.FileStorage.Abstractions;
using GM.FileStorage.Catalog;
using GM.FileStorage.Keys;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using GM.FileStorage.MalwareScanner;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.UploadFile;

/// <summary>
/// Stores a new file through <see cref="IFileStorageService"/> and records it in the catalog: a
/// <see cref="StoredFile"/> row, identified by a new GUID, with the caller's tenant (the uploader is its
/// <c>CreatedBy</c>), so files can be listed by tenant or user. A stored file's content never changes; new content is a new file.
/// </summary>
public class UploadFileCommand : IRequest<UploadedFileDto>
{
    /// <summary>The bytes, streamed straight into storage (never buffered whole).</summary>
    public Stream? Content { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
}

public class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    public UploadFileCommandValidator()
    {
        RuleFor(x => x.Content).NotNull();
        RuleFor(x => x.FileName).NotNull().NotEmpty().MaximumLength(StoredFile.FileNameMaxLength);
        RuleFor(x => x.ContentType).NotNull().NotEmpty().MaximumLength(StoredFile.ContentTypeMaxLength);
    }
}

/// <remarks>
/// A file has one identifier, a GUID (its id, which is also its key). Where its bytes live is separate: a storage key
/// from FileStorage:KeyTemplate (e.g. <c>3f2b9c1e-…/2026/11/07/9f1c…</c>: the tenant, then the upload date).
/// <para>
/// Storage and the catalog can't share a transaction, so the handler orders its steps so that every failure
/// leaves at worst unreferenced bytes, and those are always queued for deletion (<see cref="StorageCleanup"/>):
/// </para>
/// <list type="number">
/// <item>Queue the new storage key for cleanup (due after a grace period) and save — before any bytes exist.</item>
/// <item>Upload under that key.</item>
/// <item>In one transaction: create the file row and drop the pending entry. If this fails,
/// the new bytes are deleted (or left to the job).</item>
/// </list>
/// </remarks>
public class UploadFileCommandHandler(
    IUnitOfWork unitOfWork, IFileStorageService storage, IFileKeyStrategy keyStrategy,
    ICurrentActor actor, StoredFileCatalog<StoredFile, StorageCleanup> catalog)
    : IRequestHandler<UploadFileCommand, UploadedFileDto>
{
    // How long a client is told to wait (Retry-After) when the malware scanner can't answer.
    private static readonly TimeSpan ScanRetryAfter = TimeSpan.FromSeconds(30);

    public async Task<UploadedFileDto> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        // The caller's tenant, recorded on the file (the uploader is the audit column CreatedBy). The stored object
        // carries nothing that identifies it; the tenant only names the folder its key starts with ({meta:tenantId}).
        var tenantId = actor.TenantOrShared();
        var metadata = new Dictionary<string, string>();
        var keyValues = new Dictionary<string, string> { ["tenantId"] = tenantId };

        // The file's id, and where its bytes go: a path from FileStorage:KeyTemplate built on that id, so the stored
        // object is named after the file. It's needed before the upload (it's queued first), so it's built from the
        // declared content type; with FileStorage:VerifyContentType on, a declaration the bytes don't match is
        // rejected by the upload, so the {category} folder can't lie.
        var id = Guid.NewGuid();
        var storageKey = keyStrategy.GenerateKey(new FileKeyContext(request.FileName!, keyValues) { ContentType = request.ContentType, Id = id });

        // 1. Queue the new storage key before any bytes exist, so a crash after the upload can't strand them.
        var pending = StorageCleanup.Create(storageKey, DateTime.UtcNow.Add(StorageCleanup.PendingUploadGracePeriod));
        await unitOfWork.StorageCleanupRepository.AddAsync(pending, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // 2. Store the bytes. FileStorage:MaxFileSize / AllowedContentTypes / VerifyContentType are enforced inside
        // UploadAsync before anything is kept; a rejection is the caller's mistake, so it surfaces as a 400 with the
        // reason. Any other failure (often the request stream itself: a body over the server's limit, a client gone)
        // removes whatever was partly written straight away; if even that fails, the pending entry leaves it to the job.
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
        catch (Exception ex) when (ex is FileTooLargeException or FileContentTypeNotAllowedException or FileContentTypeMismatchException
                                       or FileMalwareDetectedException)
        {
            await catalog.TryCompleteCleanupAsync(pending, CancellationToken.None);
            throw new BadRequestException(ex.Message);
        }
        catch (MalwareScanFailedException)
        {
            await catalog.TryCompleteCleanupAsync(pending, CancellationToken.None);
            throw new ServiceUnavailableException(
                "Malware scanning is on, but the scanner didn't answer. Nothing was kept; try again shortly.", ScanRetryAfter);
        }
        catch
        {
            await catalog.TryCompleteCleanupAsync(pending, CancellationToken.None);
            throw;
        }

        // 3. Record it, in one transaction.
        StoredFile entity;
        try
        {
            entity = StoredFile.Create(id, storageKey, stored.FileName, stored.ContentType, stored.Size,
                stored.Checksum, stored.Visibility == FileVisibility.Public,
                stored.LastModifiedUtc?.UtcDateTime ?? DateTime.UtcNow, tenantId);
            await unitOfWork.StoredFileRepository.AddAsync(entity, cancellationToken);

            // The bytes are referenced now, so they're no longer the pending upload's to clean up. If the job has
            // already claimed the entry (an upload slower than the grace period), this save fails instead.
            unitOfWork.StorageCleanupRepository.Remove(pending);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Not recorded: our bytes are unreferenced. Delete them now; the pending entry is still queued, so the
            // job retries if this fails.
            try { await storage.DeleteAsync(storageKey, CancellationToken.None); }
#pragma warning disable CA1031 // Best effort: the queued entry covers it.
            catch (Exception) { }
#pragma warning restore CA1031

            throw;
        }

        return new UploadedFileDto
        {
            Id = entity.Id,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            Size = entity.Size,
            Checksum = entity.Checksum,
        };
    }
}

/// <summary>The catalog record of a file that was just stored.</summary>
public class UploadedFileDto
{
    /// <summary>The file's identifier (a GUID): use it for every other call.</summary>
    public Guid Id { get; set; }

    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Checksum { get; set; }
}
