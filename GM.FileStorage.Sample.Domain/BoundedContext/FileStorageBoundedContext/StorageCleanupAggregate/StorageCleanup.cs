using GM.EntityFramework.Domain.Abstractions;
using GM.FileStorage.Domain.FileStorage.StorageCleanupAggregate.Entities;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;

/// <summary>
/// The sample's storage-cleanup aggregate root: a storage object to delete once it falls due, unless a stored file
/// (live or deleted) still points at it. Its behaviour lives in the GM.FileStorage base <see cref="GMStorageCleanup"/>.
/// Queued before an upload and removed in the transaction that records it, so an upload that fails after storing its
/// bytes never leaves them behind for good. Deleting a file queues nothing: it keeps the bytes, so it can be restored.
/// </summary>
public class StorageCleanup : GMStorageCleanup, IStorageCleanupFactory<StorageCleanup>, IAggregateRoot
{
    private StorageCleanup() { } // EF Core materialization

    private StorageCleanup(string storageKey, DateTime dueAtUtc) : base(storageKey, dueAtUtc)
    {
    }

    public static StorageCleanup Create(string storageKey, DateTime dueAtUtc) => new(storageKey, dueAtUtc);
}
