using GM.EntityFramework.Domain.Abstractions;
using GM.FileStorage.Domain.FileStorage.StoredFileAggregate.Entities;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;

/// <summary>
/// The sample's concrete stored-file aggregate root. Its shape and behaviour live in the GM.FileStorage base
/// <see cref="GMStoredFile{TStoredFile,TStoredFileMetadata}"/>; this type fixes it as the application's aggregate
/// and exposes the factory, mirroring how the other GM samples specialise their library bases.
/// A file has one identifier, its id (a GUID). Its bytes live at the <c>StorageKey</c>, a folder path from the key
/// template built on that id (<c>documents/6f/6f1e….pdf</c> for file <c>6f1e…</c>). A file's content never changes
/// after upload.
/// </summary>
public class StoredFile : GMStoredFile<StoredFile, StoredFileMetadata>, IAggregateRoot
{
    private StoredFile() { } // EF Core materialization

    private StoredFile(
        Guid id,
        string storageKey,
        string fileName,
        string contentType,
        long size,
        string? checksum,
        bool isPublic,
        DateTime uploadedAtUtc,
        string tenantId)
        : base(id, storageKey, fileName, contentType, size, checksum, isPublic, uploadedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (tenantId.Length > TenantIdMaxLength)
            throw new ArgumentException($"Must be at most {TenantIdMaxLength} characters.", nameof(tenantId));
        TenantId = tenantId;
    }

    public const int IntegrityProblemMaxLength = 256;
    public const int TenantIdMaxLength = 64;

    /// <summary>
    /// The tenant the file belongs to: the uploader's <c>X-Tenant-Id</c>, or the shared <c>public</c> space. Every
    /// read and change is scoped to it. The uploader is the audit column <c>CreatedBy</c> (their <c>X-User-Id</c>).
    /// </summary>
    public string TenantId { get; private set; } = null!;

    /// <summary>When the file was deleted (UTC); null while it's live. Retention purges files deleted long enough ago.</summary>
    public DateTime? DeletedAtUtc { get; private set; }

    /// <summary>When the stored bytes were last checked against this record (UTC); null until the first check.</summary>
    public DateTime? VerifiedAtUtc { get; private set; }

    /// <summary>What the last check found wrong with the stored bytes (missing, wrong size, checksum mismatch); null when they matched.</summary>
    public string? IntegrityProblem { get; private set; }

    public override void SoftRemove()
    {
        base.SoftRemove();
        DeletedAtUtc = DateTime.UtcNow;
    }

    public override void RestoreAll()
    {
        base.RestoreAll();
        DeletedAtUtc = null;
    }

    /// <summary>Records a check of the stored bytes: when, and what was wrong (null when they matched).</summary>
    public void RecordVerification(DateTime verifiedAtUtc, string? problem)
    {
        if (problem is { Length: > IntegrityProblemMaxLength })
            problem = problem[..IntegrityProblemMaxLength];

        VerifiedAtUtc = DateTime.SpecifyKind(verifiedAtUtc, DateTimeKind.Utc);
        IntegrityProblem = problem;
    }

    /// <summary>
    /// Records a file, identified by <paramref name="id"/>, whose bytes were just stored at <paramref name="storageKey"/>,
    /// for <paramref name="tenantId"/>.
    /// </summary>
    public static StoredFile Create(
        Guid id,
        string storageKey,
        string fileName,
        string contentType,
        long size,
        string? checksum,
        bool isPublic,
        DateTime uploadedAtUtc,
        string tenantId) =>
        new(id, storageKey, fileName, contentType, size, checksum, isPublic, uploadedAtUtc, tenantId);
}
