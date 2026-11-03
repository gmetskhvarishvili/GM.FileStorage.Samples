using GM.EntityFramework.Domain.Abstractions;
using GM.FileStorage.Domain.FileStorage.StoredFileAggregate.Entities;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;

/// <summary>
/// The sample's concrete stored-file aggregate root. Its shape and behaviour live in the GM.FileStorage base
/// <see cref="GMStoredFile{TStoredFile,TStoredFileMetadata}"/>; this type fixes it as the application's aggregate
/// and exposes the factory, mirroring how the other GM samples specialise their library bases. Every upload is
/// stored under its own <see cref="NewStorageKey">versioned storage key</see>, so a replacement never overwrites
/// bytes the catalog still points at.
/// </summary>
public class StoredFile : GMStoredFile<StoredFile, StoredFileMetadata>, IAggregateRoot
{
    private StoredFile() { } // EF Core materialization

    private StoredFile(
        string key,
        string storageKey,
        string fileName,
        string contentType,
        long size,
        string? checksum,
        bool isPublic,
        DateTime uploadedAtUtc) : base(key, storageKey, fileName, contentType, size, checksum, isPublic, uploadedAtUtc) { }

    /// <summary>Records a file known as <paramref name="key"/> whose bytes were just stored at <paramref name="storageKey"/>.</summary>
    public static StoredFile Create(
        string key,
        string storageKey,
        string fileName,
        string contentType,
        long size,
        string? checksum,
        bool isPublic,
        DateTime uploadedAtUtc) =>
        new(key, storageKey, fileName, contentType, size, checksum, isPublic, uploadedAtUtc);

    /// <summary>A fresh storage key for one upload of <paramref name="key"/>: <c>{key}~{version}</c>.</summary>
    public static string NewStorageKey(string key) => $"{key}~{Guid.NewGuid():N}";
}
