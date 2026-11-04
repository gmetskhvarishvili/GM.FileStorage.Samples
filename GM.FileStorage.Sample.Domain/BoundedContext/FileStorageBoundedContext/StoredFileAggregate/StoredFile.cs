using GM.EntityFramework.Domain.Abstractions;
using GM.FileStorage.Domain.FileStorage.StoredFileAggregate.Entities;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;

/// <summary>
/// The sample's concrete stored-file aggregate root. Its shape and behaviour live in the GM.FileStorage base
/// <see cref="GMStoredFile{TStoredFile,TStoredFileMetadata}"/>; this type fixes it as the application's aggregate
/// and exposes the factory, mirroring how the other GM samples specialise their library bases.
/// A file has one identifier: its <see cref="GMStoredFile{TStoredFile,TStoredFileMetadata}.Key"/> is its id, a
/// plain GUID. Where the bytes live is separate — the <c>StorageKey</c>, a folder path from the key template
/// (e.g. <c>documents/9f/9f1c….pdf</c>). A file's content never changes after upload.
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
        DateTime uploadedAtUtc)
        : base(id, id.ToString(), storageKey, fileName, contentType, size, checksum, isPublic, uploadedAtUtc) { }

    public const int IntegrityProblemMaxLength = 256;

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

    /// <summary>Records a file, identified by <paramref name="id"/>, whose bytes were just stored at <paramref name="storageKey"/>.</summary>
    public static StoredFile Create(
        Guid id,
        string storageKey,
        string fileName,
        string contentType,
        long size,
        string? checksum,
        bool isPublic,
        DateTime uploadedAtUtc) =>
        new(id, storageKey, fileName, contentType, size, checksum, isPublic, uploadedAtUtc);
}
