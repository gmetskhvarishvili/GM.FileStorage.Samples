using GM.EntityFramework.Domain.Abstractions;
using GM.EntityFramework.Domain.Base;
using System;

namespace GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StorageCleanupAggregate;

/// <summary>
/// A storage object that must be deleted once <see cref="DueAtUtc"/> passes, unless a stored file (live or deleted)
/// still points at it. Written in the same transaction as the change that may orphan the bytes, so a crash or a
/// failed delete never leaves them behind for good:
/// <list type="bullet">
/// <item>before an upload, for its new storage key, due after <see cref="PendingUploadGracePeriod"/> (removed again
/// when the upload is recorded);</item>
/// <item>when a replacement is recorded, for the previous version's storage key, due now.</item>
/// </list>
/// Deleting a file queues nothing: it only marks the row deleted and keeps the bytes, so it can be restored.
/// The request deletes the bytes itself straight after saving; the storage-cleanup job retries whatever is left.
/// </summary>
public class StorageCleanup : Entity<Guid>, IAggregateRoot
{
    /// <summary>How long an upload may take before its bytes count as abandoned.</summary>
    public static readonly TimeSpan PendingUploadGracePeriod = TimeSpan.FromMinutes(15);

    private StorageCleanup() { } // EF Core materialization

    private StorageCleanup(string storageKey, DateTime dueAtUtc)
    {
        Id = Guid.NewGuid();
        StorageKey = storageKey;
        DueAtUtc = dueAtUtc;
    }

    /// <summary>The storage object to delete.</summary>
    public string StorageKey { get; private set; } = null!;

    /// <summary>When it may be deleted (UTC).</summary>
    public DateTime DueAtUtc { get; private set; }

    /// <summary>Delete attempts so far.</summary>
    public int Attempts { get; private set; }

    public static StorageCleanup Create(string storageKey, DateTime dueAtUtc) => new(storageKey, dueAtUtc);

    /// <summary>
    /// Takes this entry before deleting its bytes: moves <see cref="DueAtUtc"/> out by a backoff (1, 2, 4 … minutes,
    /// at most an hour), so if the delete fails the entry waits before its next try. <see cref="DueAtUtc"/> is a
    /// concurrency token, so once this is saved an upload still holding the entry can no longer record its bytes
    /// as live, and two cleaners can't both take it.
    /// </summary>
    public void Claim(DateTime nowUtc)
    {
        Attempts++;
        DueAtUtc = nowUtc.AddMinutes(Math.Min(60, Math.Pow(2, Attempts - 1)));
    }
}
