# Request flows

Every call below enters through the gateway, which checks the rate limit for its route and forwards it to the
files API. The gateway step is drawn once, in the upload; the other diagrams start at the API.

The bytes (in storage) and the row (in Postgres) can't share a transaction. Each write is ordered so that any
failure leaves at worst bytes nothing points at, and those are always queued in `storage_cleanups` for the
worker to delete.

## Upload — `POST /api/v1/files`

```mermaid
sequenceDiagram
    autonumber
    actor C as Client
    participant G as Gateway
    participant A as Files API
    participant DB as PostgreSQL
    participant S as Storage

    C->>G: POST /api/v1/files (multipart file, tenantId, userId)
    alt over 30 uploads/min from this IP
        G-->>C: 429 Too Many Requests + Retry-After
    end
    G->>A: forward (body streamed)
    A->>A: storage key from KeyTemplate<br/>documents/9f/9f1c….pdf
    A->>DB: queue the key in storage_cleanups (due in 15 min)
    A->>S: upload the bytes (size, type and signature checked)
    alt rejected (too large, type not allowed, bytes don't match type)
        A->>S: delete what was written
        A->>DB: drop the queued entry
        A-->>G: 400 with the reason
    end
    A->>DB: one transaction: insert stored_files + metadata rows,<br/>drop the queued entry, domain event
    A-->>G: 200 { id, storageKey: "9f1c….pdf", … }
    G-->>C: 200 + X-RateLimit-* headers
```

If the API crashes after step 6, the bytes exist but nothing points at them. The queued entry falls due 15
minutes later and the worker deletes them.

## Replace — `PUT /api/v1/files/{id}/content`

```mermaid
sequenceDiagram
    autonumber
    participant A as Files API
    participant DB as PostgreSQL
    participant S as Storage

    A->>DB: load the file (404 if unknown) and its tenant/user
    A->>A: a fresh storage key for the new version
    A->>DB: queue the new key (due in 15 min)
    A->>S: upload the new bytes
    A->>DB: one transaction: point the row at the new key<br/>(checked against the key read in step 1),<br/>sync metadata, drop the pending entry,<br/>queue the old key (due now)
    alt another replacement saved first
        A->>S: delete the new bytes
        A-->>A: 400 "Another upload … finished first. Retry."
    end
    A->>S: delete the old version
    A->>DB: drop its entry (left queued if the delete failed)
```

- The id stays the same; only the storage key changes. A replacement never overwrites bytes the catalog still
  points at.
- Tenant and user stay as they were unless the request gives new ones.
- Replacing a deleted file restores it.

## Delete and restore

```mermaid
sequenceDiagram
    participant A as Files API
    participant DB as PostgreSQL
    participant S as Storage

    Note over A,DB: DELETE /api/v1/files/{id}
    A->>DB: mark the row and its metadata deleted
    Note over S: bytes untouched

    Note over A,DB: POST /api/v1/files/{id}/restore
    A->>DB: clear the deleted flag on the row and its metadata
    Note over S: nothing to copy back
```

A deleted file is hidden from the list, details, download and presign (except with `visibility=All`). Its
bytes are never queued for cleanup, so a restore is instant.

## Download — `GET /api/v1/files/{id}/content`

```mermaid
sequenceDiagram
    actor C as Client
    participant A as Files API
    participant DB as PostgreSQL
    participant S as Storage

    C->>A: GET …/content (optional Range: bytes=0-1023)
    A->>DB: find the live file → storage key
    A->>S: open a stream (decrypted when encryption is on)
    alt Range header
        A-->>C: 206 Partial Content (or 416 if out of range)
    else
        A-->>C: 200, streamed
    end
```

## Cleanup — the worker, every minute

```mermaid
sequenceDiagram
    autonumber
    participant W as StorageCleanupJob
    participant DB as PostgreSQL
    participant S as Storage

    W->>W: take the scheduling lock (one run at a time)
    W->>DB: up to 100 entries with due_at_utc ≤ now
    loop each entry, in its own scope
        alt a file row still uses this storage key
            W->>DB: drop the entry, keep the bytes
        else
            W->>DB: claim it: attempts + 1, push due_at_utc to<br/>1, 2, 4 … 60 min ahead (fails if someone else changed it)
            W->>S: delete the bytes
            alt deleted
                W->>DB: drop the entry
            else failed
                Note over W,DB: stays claimed; retried when due again
            end
        end
    end
```

Claiming by the `due_at_utc` concurrency token means the job and an upload slower than its 15-minute grace
period can't both act on the same entry: whichever saves second fails and backs off.
