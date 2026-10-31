using GM.EntityFramework.Domain.Abstractions;
using GM.FileStorage.Domain.FileStorage.StoredFileAggregate.Entities;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;

/// <summary>
/// The sample's concrete stored-file aggregate root. Its shape and behaviour live in the GM.FileStorage base
/// <see cref="GMStoredFile{TStoredFile,TStoredFileMetadata}"/>; this type fixes it as the application's aggregate
/// and exposes the factory, mirroring how the other GM samples specialise their library bases.
/// </summary>
public class StoredFile : GMStoredFile<StoredFile, StoredFileMetadata>, IAggregateRoot
{
    private StoredFile() { } // EF Core materialization

    private StoredFile(
        string key,
        string fileName,
        string contentType,
        long size,
        string? checksum,
        bool isPublic,
        DateTime uploadedAtUtc) : base(key, fileName, contentType, size, checksum, isPublic, uploadedAtUtc) { }

    /// <summary>Records a file that was just stored at <paramref name="key"/>.</summary>
    public static StoredFile Create(
        string key,
        string fileName,
        string contentType,
        long size,
        string? checksum,
        bool isPublic,
        DateTime uploadedAtUtc) =>
        new(key, fileName, contentType, size, checksum, isPublic, uploadedAtUtc);
}
