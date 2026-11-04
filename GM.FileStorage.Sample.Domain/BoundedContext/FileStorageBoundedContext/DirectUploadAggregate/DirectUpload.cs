using GM.EntityFramework.Domain.Abstractions;
using GM.EntityFramework.Domain.Base;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.DirectUploadAggregate;

/// <summary>
/// A large file on its way straight into storage: the client was given a presigned link to write
/// <see cref="StorageKey"/> and, once it has, asks for the file to be recorded. Until then the bytes (if any) belong
/// to nobody, so the storage key is also queued for cleanup; recording the file removes both. Its id is what the
/// client completes the upload with.
/// </summary>
public class DirectUpload : Entity<Guid>, IAggregateRoot
{
    public const int FileNameMaxLength = 512;
    public const int ContentTypeMaxLength = 255;
    public const int MetadataValueMaxLength = 512;

    private DirectUpload() { } // EF Core materialization

    private DirectUpload(string storageKey, string fileName, string contentType, long size, string tenantId,
        string userId, DateTime expiresAtUtc)
    {
        Id = Guid.NewGuid();
        StorageKey = storageKey;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        TenantId = tenantId;
        UserId = userId;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Where the client writes the bytes.</summary>
    public string StorageKey { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    /// <summary>The declared type; the stored bytes are checked against it when the upload completes.</summary>
    public string ContentType { get; private set; } = null!;

    /// <summary>The declared size in bytes; the stored object must match it.</summary>
    public long Size { get; private set; }

    public string TenantId { get; private set; } = null!;
    public string UserId { get; private set; } = null!;

    /// <summary>When the write link expires (UTC); the upload can't be completed after that.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    public static DirectUpload Create(string storageKey, string fileName, string contentType, long size,
        string tenantId, string userId, DateTime expiresAtUtc) =>
        new(storageKey, fileName, contentType, size, tenantId, userId, expiresAtUtc);
}
