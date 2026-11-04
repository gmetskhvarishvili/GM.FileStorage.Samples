using FluentValidation;
using GM.EntityFramework.Domain.Common;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.DirectUploadAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.StartDirectUpload;

/// <summary>
/// Begins a direct upload: for files too large to send through the API, the client gets a presigned link to write
/// the bytes straight into storage, then completes the upload (<c>CompleteDirectUploadCommand</c>) to have the file
/// recorded. Nothing is stored yet.
/// </summary>
public class StartDirectUploadCommand : IRequest<DirectUploadDto>
{
    public string? FileName { get; set; }
    public string? ContentType { get; set; }

    /// <summary>The file's exact size in bytes; the stored object must match it.</summary>
    public long Size { get; set; }
}

public class StartDirectUploadCommandValidator : AbstractValidator<StartDirectUploadCommand>
{
    public StartDirectUploadCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(DirectUpload.FileNameMaxLength);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(DirectUpload.ContentTypeMaxLength);
        RuleFor(x => x.Size).GreaterThan(0);
    }
}

/// <remarks>
/// The size and type rules are checked now, before any bytes move, and again on completion against what was really
/// stored. The storage key is queued for cleanup in the same transaction as the pending upload, due once the link has
/// expired (plus the usual grace period): if the client never completes, whatever it wrote is deleted.
/// </remarks>
public class StartDirectUploadCommandHandler(
    IUnitOfWork unitOfWork,
    IFileStorageService storage,
    IFileKeyStrategy keyStrategy,
    IOptions<FileStorageOptions> storageOptions,
    IOptions<DirectUploadOptions> options,
    IOptions<QuotaOptions> quota,
    ICurrentActor actor)
    : IRequestHandler<StartDirectUploadCommand, DirectUploadDto>
{
    public async Task<DirectUploadDto> Handle(StartDirectUploadCommand request, CancellationToken cancellationToken)
    {
        var limits = options.Value;
        if (request.Size > limits.MaxFileSize)
            throw new BadRequestException($"The file is larger than the {limits.MaxFileSize}-byte limit for direct uploads.");
        if (!UploadRules.IsAllowed(storageOptions.Value, request.ContentType!))
            throw new BadRequestException($"Content type '{request.ContentType}' is not allowed.");

        var tenantId = actor.TenantOrShared();
        var userId = actor.UserOrAnonymous();

        // The declared size counts against the tenant's quota from now (the pending upload is part of its usage), and
        // completing it can't go over: the stored object must have exactly that size.
        await TenantQuota.EnsureRoomAsync(unitOfWork, quota.Value, tenantId, request.Size, cancellationToken);

        var metadata = new Dictionary<string, string>
        {
            [StoredFileMetadataNames.TenantId] = tenantId,
            [StoredFileMetadataNames.UserId] = userId,
        };
        var storageKey = keyStrategy.GenerateKey(new FileKeyContext(request.FileName!, metadata) { ContentType = request.ContentType });

        Uri url;
        try
        {
            url = await storage.GetPresignedUrlAsync(storageKey,
                new PresignedUrlRequest { Operation = PresignedUrlOperation.Write, Expiry = limits.LinkExpiry }, cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            // Encryption at rest (a client can't encrypt for us), or local disk without signed links.
            throw new BadRequestException($"Direct uploads aren't available here: {ex.Message}");
        }

        var expiresAtUtc = DateTime.UtcNow.Add(limits.LinkExpiry);
        var upload = DirectUpload.Create(storageKey, request.FileName!, request.ContentType!, request.Size, tenantId, userId, expiresAtUtc);
        await unitOfWork.DirectUploadRepository.AddAsync(upload, cancellationToken);
        await unitOfWork.StorageCleanupRepository.AddAsync(
            StorageCleanup.Create(storageKey, expiresAtUtc.Add(StorageCleanup.PendingUploadGracePeriod)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new DirectUploadDto
        {
            UploadId = upload.Id,
            Url = url,
            Method = "PUT",
            // Azure needs the blob type on a PUT; every backend takes the content type.
            Headers = storageOptions.Value.Provider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, string> { ["x-ms-blob-type"] = "BlockBlob", ["Content-Type"] = request.ContentType! }
                : new Dictionary<string, string> { ["Content-Type"] = request.ContentType! },
            ExpiresAtUtc = expiresAtUtc,
        };
    }
}

/// <summary>Where and how to send the bytes, and the id to complete the upload with.</summary>
public class DirectUploadDto
{
    public Guid UploadId { get; set; }

    /// <summary>The presigned write link.</summary>
    public Uri? Url { get; set; }

    /// <summary>The HTTP method to send the bytes with.</summary>
    public string? Method { get; set; }

    /// <summary>Headers the request must carry.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    /// <summary>When the link expires (UTC); upload and complete before then.</summary>
    public DateTime ExpiresAtUtc { get; set; }
}
