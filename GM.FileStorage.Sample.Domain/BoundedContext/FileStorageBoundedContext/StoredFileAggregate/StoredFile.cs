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
