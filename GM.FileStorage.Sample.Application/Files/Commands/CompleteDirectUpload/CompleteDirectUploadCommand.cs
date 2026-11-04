using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Application.Files.Commands.UploadFile;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.DirectUploadAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.CompleteDirectUpload;

/// <summary>
/// Records a direct upload once the client has written the bytes: checks what's really in storage against what was
/// declared, then creates the file like any other upload. Refused bytes are deleted.
/// </summary>
public class CompleteDirectUploadCommand : IRequest<UploadedFileDto>
{
    public Guid UploadId { get; set; }
}

/// <remarks>
/// Checks, against the stored object rather than the client's word: it exists; its size is the declared one (and
/// within the limit); with <c>FileStorage:VerifyContentType</c>, its first bytes are the declared type. The bytes are
/// never read through the API, so the file has no checksum.
/// </remarks>
public class CompleteDirectUploadCommandHandler(
    IUnitOfWork unitOfWork,
    IFileStorageService storage,
    IOptions<FileStorageOptions> storageOptions,
    IOptions<DirectUploadOptions> options)
    : IRequestHandler<CompleteDirectUploadCommand, UploadedFileDto>
{
    public async Task<UploadedFileDto> Handle(CompleteDirectUploadCommand request, CancellationToken cancellationToken)
    {
        var upload = await unitOfWork.DirectUploadRepository
            .FirstOrDefaultAsync(x => x.Id == request.UploadId, false, null, cancellationToken)
            ?? throw new NotFoundException(StringResource.File, StringResource.Id, request.UploadId);

        if (upload.ExpiresAtUtc < DateTime.UtcNow)
        {
            // Too late: whatever was written is left to its queued cleanup.
            unitOfWork.DirectUploadRepository.Remove(upload);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new BadRequestException("The upload link has expired. Start the upload again.");
        }

        if (!await storage.ExistsAsync(upload.StorageKey, cancellationToken))
            throw new BadRequestException("Nothing has been uploaded to the link yet.");

        var (size, header) = await ReadHeaderAsync(upload.StorageKey, cancellationToken);
        var problem =
            size != upload.Size ? $"The uploaded file is {size} bytes, not the {upload.Size} declared."
            : size > options.Value.MaxFileSize ? $"The file is larger than the {options.Value.MaxFileSize}-byte limit for direct uploads."
            : storageOptions.Value.VerifyContentType && !FileTypeDetector.Matches(upload.ContentType, header)
                ? $"The file's content isn't '{upload.ContentType}'."
            : null;
        if (problem is not null)
        {
            await DiscardAsync(upload, cancellationToken);
            throw new BadRequestException(problem + " It was deleted; start the upload again.");
        }

        var pending = await unitOfWork.StorageCleanupRepository
            .FirstOrDefaultAsync(x => x.StorageKey == upload.StorageKey, false, null, cancellationToken);

        var entity = StoredFile.Create(Guid.NewGuid(), upload.StorageKey, upload.FileName, upload.ContentType, size,
            checksum: null, isPublic: false, DateTime.UtcNow);
        await unitOfWork.StoredFileRepository.AddAsync(entity, cancellationToken);
        foreach (var (name, value) in new Dictionary<string, string>
                 {
                     [StoredFileMetadataNames.TenantId] = upload.TenantId,
                     [StoredFileMetadataNames.UserId] = upload.UserId,
                 })
        {
            await unitOfWork.StoredFileMetadataRepository.AddAsync(StoredFileMetadata.Create(entity.Id, name, value), cancellationToken);
        }

        // The bytes are referenced now: they're no longer the cleanup queue's (if the job has claimed the entry in
        // the meantime, this save fails and nothing is recorded).
        unitOfWork.DirectUploadRepository.Remove(upload);
        if (pending is not null)
            unitOfWork.StorageCleanupRepository.Remove(pending);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UploadedFileDto
        {
            Id = entity.Id,
            StorageKey = StorageKeys.Name(entity.StorageKey),
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            Size = entity.Size,
            Checksum = entity.Checksum,
        };
    }

    // The stored object's real size and its first bytes, without reading the rest.
    private async Task<(long Size, byte[] Header)> ReadHeaderAsync(string storageKey, CancellationToken cancellationToken)
    {
        await using var first = await storage.DownloadAsync(storageKey, new FileByteRange(0, FileTypeDetector.HeaderLength), cancellationToken);
        using var copy = new MemoryStream();
        await first.Content.CopyToAsync(copy, cancellationToken);
        return (first.Metadata.Size, copy.ToArray());
    }

    // Deletes refused bytes now (their queued entry covers a failure) and forgets the upload.
    private async Task DiscardAsync(DirectUpload upload, CancellationToken cancellationToken)
    {
        var pending = await unitOfWork.StorageCleanupRepository
            .FirstOrDefaultAsync(x => x.StorageKey == upload.StorageKey, false, null, cancellationToken);
        unitOfWork.DirectUploadRepository.Remove(upload);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (pending is not null)
            await StorageCleanups.TryCompleteAsync(unitOfWork, storage, pending, CancellationToken.None);
    }
}
