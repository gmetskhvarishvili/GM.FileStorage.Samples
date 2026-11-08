using GM.EntityFramework.Domain.Abstractions;
using GM.FileStorage.Domain.FileStorage.StoredFileAggregate.Entities;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;

/// <summary>
/// The sample's concrete stored-file aggregate root. Its shape and behaviour (tenant, soft delete, downloads,
/// integrity checks) live in the GM.FileStorage base <see cref="GMStoredFile{TStoredFile}"/>; this type fixes it as the
/// application's aggregate and exposes the factory, mirroring how the other GM samples specialise their library bases.
/// A file has one identifier, its id (a GUID). Its bytes live at the <c>StorageKey</c>, a folder path from the key
/// template built on that id. A file's content never changes after upload.
/// </summary>
public class StoredFile : GMStoredFile<StoredFile>, IAggregateRoot
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
        : base(id, storageKey, fileName, contentType, size, checksum, isPublic, uploadedAtUtc, tenantId)
    {
    }

    /// <summary>
    /// Records a file, identified by <paramref name="id"/>, whose bytes were just stored at <paramref name="storageKey"/>,
    /// for <paramref name="tenantId"/>: the uploader's <c>X-Tenant-Id</c>, or the shared <c>public</c> space. The
    /// uploader is the audit column <c>CreatedBy</c> (their <c>X-User-Id</c>).
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
