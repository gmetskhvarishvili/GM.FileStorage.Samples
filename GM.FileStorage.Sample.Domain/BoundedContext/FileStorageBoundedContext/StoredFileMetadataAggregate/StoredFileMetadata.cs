using GM.EntityFramework.Domain.Abstractions;
using GM.FileStorage.Domain.FileStorage.StoredFileMetadataAggregate.Entities;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;

/// <summary>
/// One searchable metadata pair of a <see cref="StoredFile"/> (e.g. <c>tenantId = acme</c>). The behaviour lives in
/// the GM.FileStorage base <see cref="GMStoredFileMetadata{TStoredFile,TStoredFileMetadata}"/>.
/// </summary>
public class StoredFileMetadata : GMStoredFileMetadata<StoredFile, StoredFileMetadata>, IAggregateRoot
{
    private StoredFileMetadata() { } // EF Core materialization

    private StoredFileMetadata(Guid storedFileId, string name, string value) : base(storedFileId, name, value) { }

    public static StoredFileMetadata Create(Guid storedFileId, string name, string value) =>
        new(storedFileId, name, value);
}
