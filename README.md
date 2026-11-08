<p align="center">
  <img src="icon.png" alt="GM.FileStorage Samples" width="140" height="140" />
</p>

# GM.FileStorage Samples

[![CI](https://github.com/gmetskhvarishvili/GM.FileStorage.Samples/actions/workflows/ci.yml/badge.svg)](https://github.com/gmetskhvarishvili/GM.FileStorage.Samples/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A layered **DDD + CQRS** file-storage sample built on the **GM.\*** ecosystem: streaming upload/download,
keys organised by file type and delete through the
provider-agnostic `IFileStorageService` — plus a **PostgreSQL catalog** of every stored file and its
metadata, so each tenant's files can be listed, inspected by id, soft-deleted and restored. Targets
**.NET 10** and **PostgreSQL**.

It assembles **[GM.FileStorage](https://www.nuget.org/packages/GM.FileStorage)** with its
**[Local](https://www.nuget.org/packages/GM.FileStorage.Local)**,
**[S3](https://www.nuget.org/packages/GM.FileStorage.S3)** and
**[AzureBlob](https://www.nuget.org/packages/GM.FileStorage.AzureBlob)** providers,
**[GM.FileStorage.Domain](https://www.nuget.org/packages/GM.FileStorage.Domain)** +
**[GM.FileStorage.Persistence](https://www.nuget.org/packages/GM.FileStorage.Persistence)** (the stored-file
catalog), **[GM.API](https://www.nuget.org/packages/GM.API)** (Web API startup, versioning, Swagger, errors,
request logging, health, secrets), **[GM.Mediator](https://www.nuget.org/packages/GM.Mediator)** (CQRS
dispatch) and **[GM.EntityFramework](https://www.nuget.org/packages/GM.EntityFramework)** (repository / unit of
work / domain-event store / actor auditing). Clients reach it through a
**[GM.Gateway](https://www.nuget.org/packages/GM.Gateway)** edge with per-IP rate limits, and a separate
worker runs the storage-cleanup job on **[GM.Scheduling](https://www.nuget.org/packages/GM.Scheduling)**.
The layout mirrors [GM.Identity.Samples](https://github.com/gmetskhvarishvili/GM.Identity.Samples).

```mermaid
---
title: System overview — flowchart
---
flowchart LR
    client([Client]) --> gw[Gateway.API<br/>:7141 · rate limits]
    gw --> api[Files API<br/>:7140]
    api --> db[(PostgreSQL)]
    api --> store[(Storage)]
    worker[Worker<br/>cleanup every minute] --> db
    worker --> store
```

**Docs** ([all diagrams, by action and by doc](docs/README.md))

| Doc | Diagrams |
| --- | --- |
| [Architecture](docs/architecture.md) | Processes, layers, data model, scaling out |
| [Dependencies](docs/dependencies.md) | Runtime calls, project references, packages, classes behind a request, domain model |
| [User flows](docs/user-flows.md) | File lifecycle, the gateway's checks, and one flowchart per action with every outcome |
| [Sequence diagrams](docs/sequence-diagrams.md) | Startup, the gateway, every endpoint, encryption, cleanup, health, Swagger |
| [Configuration](docs/configuration.md) | Where each setting and secret comes from |

> Every `GM.FileStorage*` reference here is pinned to **3.0.0** (the packages release in lockstep). 2.0.0 added
> encryption at rest, key templates and upload rules, and removed `GMStoredFile.Replace`: a file's content
> never changes after upload. 2.1.0 signs local-disk presigned links, 2.2.0 records checksums measured after
> upload, 3.0.0 removes `GMStoredFile.Key` (a file is its id), 3.1.0 adds a stored file without metadata, and
> 3.2.0 seals an encrypted file's name, type and metadata so storage learns nothing about it.

## Layers

| Project | Role |
| --- | --- |
| `GM.FileStorage.Sample.Domain` | `StoredFile` and `StorageCleanup` aggregates (only constructors and `Create`: their state and behaviour are GM.FileStorage's `GMStoredFile<TStoredFile>` and `GMStorageCleanup`), repository interfaces, `StoredFileSpecification` (visibility, tenant, uploader, content type, paging), the read-only `DomainEventLog` audit view with `DomainEventLogSpecification` (by aggregate, event type and actor: a file's history), `IUnitOfWork` |
| `GM.FileStorage.Sample.Application` | Commands and queries (`Files/Commands/UploadFile`, `DeleteFile`, `RestoreFile`; `Files/Queries/GetFilesList`, `GetFileDetails`, `DownloadFile`) — request, FluentValidation validator and GM.Mediator handler in one file; DTOs are mapped by name with Mapster (GM.API formats the audit dates); cleanup, purge and tenant usage come from GM.FileStorage.Catalog |
| `GM.FileStorage.Sample.Persistence` | `ApplicationDbContext` (GM.EntityFramework `GenericDbContext` + domain-event dispatch), the GM.FileStorage configurations mapped to `file_storage.stored_files` (`FileStorageTableNames`: the library's names without the `gm_` prefix), repositories, unit of work, migrations, `AddPersistence` (Npgsql + snake_case + actor auditing) |
| `GM.FileStorage.Sample.Infrastructure` | `AddInfrastructure` (API): GM.FileStorage + the Local/S3/AzureBlob providers, encryption, the database readiness check. `AddWorkerInfrastructure` (worker): the providers plus the storage-cleanup, retention and integrity-check jobs on GM.Scheduling, which run GM.FileStorage.Catalog's batches |
| `GM.FileStorage.Sample.Common` | `StringResource` (display names, error parts) and the `GMExceptionsResource` messages GM.Exceptions formats |
| `GM.FileStorage.Sample.API` | GM.API host; `Files/FilesController` sends mediator requests and maps its `*Model`s with Mapster |
| `GM.FileStorage.Sample.Gateway.API` | The public edge: GM.Gateway (YARP) forwarding to the API, GM.RateLimiting per IP (uploads 30/min, everything else 300/min), Swagger UI at `/`, readiness that checks the API |
| `GM.FileStorage.Sample.Worker` | Generic host that runs `StorageCleanupJob` every minute; no HTTP surface |
| `tests/GM.FileStorage.Sample.Tests` | End-to-end tests over HTTP (`GmWebApplicationFactory<Program>`) and aggregate guards |

## What it demonstrates

- **One GUID per file.** A file is identified by a GUID, its id, and that's all a caller sees; every route
  uses it. Its bytes are stored under folders from `FileStorage:KeyTemplate`, in an object named after the id
  (see below), which stays internal.
- **Bytes in storage, facts in the catalog.** `UploadFile` streams the request into `IFileStorageService`,
  then records a `StoredFile` row, with the caller's tenant (`tenant_id`) and uploader (`created_by`), and saves
  through the unit of work. If the save fails, the uploaded bytes are deleted and the error rethrown, so
  storage never keeps unreferenced objects.
- **Uploads stream, never buffer.** The upload endpoints take no `IFormFile` (binding one makes ASP.NET buffer the
  whole form, to disk past 64 KB, before any code runs). They read file parts straight off the request
  (GM.API's `MultipartFiles` and `[StreamedMultipartUpload]`) into storage, so a file over `FileStorage:MaxFileSize` is
  cut off as soon as it passes the limit, without reading the rest. Swagger still shows the file pickers.
- **Batch uploads.** `POST /files/batch` takes up to 20 files and stores each as a single upload would (its own
  id, rules and transaction), answering with one result per file. A file that fails doesn't stop the rest, so a
  client resends only the failed ones.
- **Refusing a request after storing part of it.** Streaming means a second file (on the single endpoint) or a 21st
  (in a batch) is only seen after earlier ones are stored. Those are then discarded — rows removed, bytes queued
  for cleanup and deleted — and the request gets a 400 with nothing kept.
- **Files counted per client, not requests.** Every uploaded file takes a permit from
  `RateLimiting:Policies:uploaded-files` (30 a minute per client), single and batch uploads alike: a batch of
  20 counts as 20. A batch that runs out mid-way is refused whole with `429` + `Retry-After`. The gateway still
  limits requests on top.
- **Safe retries.** A `POST` sent with an `Idempotency-Key` header (any unique value, such as a new GUID per upload)
  runs once: a retry with the same key gets the first response back, with `Idempotency-Replayed: true`, and stores
  nothing and counts no file. That covers a single upload and a batch. Only
  successful responses are kept (24 hours), so a refused upload can be fixed and resent under the same key. A retry
  while the first request is still running gets `409`. The key alone decides: a different file under a used key gets
  the first file's answer. This is GM.API's built-in idempotency (GM.Idempotency), the same as GM.Identity's; the
  sample only lengthens `Idempotency:LockExpiry` to 10 minutes so a slow upload keeps its key locked.
- **Ready to scale out.** With `Redis:ConnectionString` set, the gateway's limits, the per-client file count,
  idempotency keys and the worker's job lock live in Redis, shared by every instance (compose runs Redis and two
  workers). Without it each service keeps them in memory, which suits one instance.
- **Integrity checks.** Every 5 minutes the worker reads a batch of stored files back (decrypted when encryption is
  on) and checks them against the catalog: missing bytes, a wrong size or a checksum mismatch is recorded on the file
  as `integrityProblem` (with `verifiedAtUtc`), shown in details and the list, which filters on
  `integrityProblem=true`. A file recorded without a checksum gets one here.
- **Malware scanning, when you switch it on.** With `FileStorage:MalwareScanner:Enabled` (off by default), every
  upload is scanned by [ClamAV](https://www.clamav.net) as it streams into storage, before it's encrypted
  ([GM.FileStorage.MalwareScanner](https://www.nuget.org/packages/GM.FileStorage.MalwareScanner)). An infected file gets a `400` naming
  what was found, and nothing is kept. While ClamAV can't answer, uploads are refused unscanned with `503` and
  `Retry-After`, and readiness reports it. The integrity check rescans files too, so one matching a signature added
  after its upload is flagged. ClamAV is free; it needs about 1.5 GB of RAM. See [Running in Docker](#running-in-docker).
- **Retention.** A delete keeps the bytes so a restore is instant, but not forever: files deleted more than
  `Retention:DeletedFileDays` (30) ago are purged by the worker, rows and bytes.
- **Storage quotas.** `Quotas:BytesPerTenant` (1 GB) caps what a tenant stores, deleted files included until purged.
  An upload that would go past it gets a `400`, and its bytes are deleted.
  `GET /api/v1/files/usage` shows the usage.
- **Content never changes.** There is no replace endpoint: once uploaded, a file's bytes stay as they were.
  New content is a new upload with a new id.
- **Soft delete, easy restore.** `DeleteFile` only marks the row deleted (hidden from list, details and
  download; visible with `visibility=All`). The bytes stay in storage, so `RestoreFile` brings the file back
  exactly as it was, without a re-upload.
- **Who opened what.** Every download is recorded with who did it (user, client, session, IP, user agent,
  correlation id), next to the upload, delete and restore. `GET /api/v1/files/{id}/history` lists them, newest first
  by default, and filters, orders and pages them like the files list.
- **Whole files only.** A download is always the whole file, streamed; `Range` headers are ignored.
- **Every file belongs to a tenant.** The caller's tenant and user come from GM's actor headers, `X-Tenant-Id`
  and `X-User-Id`, through `ICurrentActor`, as in every GM service. Uploads are recorded under them, the list
  and usage only ever show the caller's tenant, and another tenant's file is a `404` on every endpoint, as if it
  didn't exist. Both are optional: without `X-Tenant-Id` the caller works in a shared space, `public`, that no
  tenant sees into (a tenant id is a GUID, so none can claim it); without `X-User-Id` the uploader is left empty. The
  gateway doesn't sign callers in: it passes the headers through as sent, so it's not a security boundary on its own.
  Put sign-in in front of it (with GM.Identity, its gateway's `AddGMActorForwarding()` sets them from the token).
- **Tenant and uploader are columns.** `tenant_id` (indexed) scopes every query; the uploader is GM's audit
  column `created_by`, filled from `X-User-Id`, and responses show both as `tenantId` and `createdBy`. The list
  filters by uploader (`userId`), content type and integrity problems, with GM.API paging (`X-Pagination` header).
  There's no metadata table: GM.FileStorage's name/value metadata (`GMStoredFile<TStoredFile, TStoredFileMetadata>`)
  is for labels that vary from file to file, which this sample doesn't take.
- **Domain events.** GM.FileStorage's `GMStoredFileUploaded/Downloaded/Deleted/RestoredDomainEvent`s are written to `domain_events.domain_events` in the same transaction, with the
  caller's identity, and dispatched in-process after the save. The file history reads them back.
- **All three backends wired at once** — `FileStorage:Provider` (default `"Local"`) chooses which one is
  active, so switching disk → S3 → Azure is a **config change, not a code change**. The cloud clients are
  only constructed when their provider is selected.
- **GM.API hosting** — Swagger UI at `/` (styled by `Assets/`), health checks at `/health/live|ready|startup`
  (ready includes the database), request logging that describes upload/download bodies instead of reading
  them, RFC 7807 errors (`NotFoundException` → 404), and secrets layered into configuration.

## Storage layout

A file is identified by a GUID, for example `9f1cb2d4-…`. Where its bytes are stored is its **storage key**,
made from `FileStorage:KeyTemplate` in appsettings.json and built on that id:

```jsonc
"KeyTemplate": "{meta:tenantId}/{date:yyyy}/{date:MM}/{date:dd}/{id}"
// tenant 3f2b9c1e-…, uploaded 7 Nov 2026 → 3f2b9c1e-…/2026/11/07/9f1cb2d4…
```

```
file-storage/
├── 3f2b9c1e-5d4a-4e7b-9a1c-2b8d6e0f4a71/   a tenant (the shared space is public/)
│   ├── 2025/12/31/…
│   └── 2026/11/07/
│       ├── 9f1cb2d4…              the bytes, encrypted
│       └── 9f1cb2d4….gmmeta       the Local provider's sidecar: placeholders and the sealed details
└── public/
    └── 2026/11/07/…
```

The storage key never leaves the API: callers only ever use the id.

- **Easy to find by tenant and date.** One tenant's files of one year, month or day are one folder (one prefix in a
  bucket), so they can be sized up, copied, or given a lifecycle rule (e.g. "move `*/2025/` to cold storage")
  without asking the database.
- **No type, no name.** No `{category}` folder and no `{ext}`: someone browsing the bucket or the disk can't
  tell a passport scan from a video. Together with encryption at rest (below), what's stored is an unreadable
  object named by an id, and the only way to open it is through the API. The path does show the tenant id and the
  upload date (UTC); nothing else about the file.
- **`{meta:tenantId}` is the caller's tenant.** The upload hands it to the key strategy only; the stored object's
  own metadata still carries nothing identifying.
- **`{id}` is the file's id.** The sample picks the id first and hands it to the key strategy
  (`FileKeyContext.Id`); `{id}` is required, so storage keys never collide.
- **Each file keeps the key it was stored under** (it's on its row), so files stored under an earlier layout, such
  as `{shard}/{id}`, stay where they are and keep working.
- **Other layouts are a config change**: `{shard}/{id}` shows nothing at all (the first two characters of the id,
  spreading files over 256 folders); `{category}/{shard}/{id}{ext}` gives `documents/9f/9f1c….pdf`. The template
  is checked at startup, so a typo fails fast.


## Keeping storage and the database in step

The bytes (in `IFileStorageService`) and the catalog row (in Postgres) can't share a transaction. The
sample orders its steps so that any failure leaves at worst bytes nothing points at, and those are
always queued for deletion in the `storage_cleanups` table.

- **Upload** ([UploadFileCommand](GM.FileStorage.Sample.Application/Files/Commands/UploadFile/UploadFileCommand.cs)):
  1. Queue the new storage key for cleanup, due in 15 minutes, before any bytes exist.
  2. Upload the bytes.
  3. In one transaction: create the row and drop the queued entry.

  | If this happens… | …the result |
  | --- | --- |
  | A crash after step 2 | The bytes are unreferenced and queued; the job deletes them. |
  | Step 3 fails | No row is created, and the new bytes are deleted (or left queued). |
- **Content never changes**, so stored bytes are never overwritten or swapped out from under a row.
- **Delete** only marks the row deleted and keeps its bytes, so nothing is queued and a restore is instant.
- **`StorageCleanupJob`** ([GM.Scheduling](https://www.nuget.org/packages/GM.Scheduling), every minute, in the
  worker):
  - It carries out due entries, backing off on failures (1, 2, 4 … minutes, at most an hour).
  - An entry whose bytes any file row still uses (live, or deleted and restorable) is dropped, not deleted.
  - Entries are claimed with a concurrency token, so the job and an upload slower than the 15-minute
    grace period can't both act on one.

## Encryption at rest

On, in the API's and the worker's appsettings.json (the worker's integrity check reads files back). The master key
is a secret, so it lives in `appsecrets.json` with the other secrets, never in appsettings.json; without one,
storage fails at first use instead of quietly writing plaintext. The committed appsecrets.json files carry a demo key
(`local-dev`); docker-compose passes its own:

```jsonc
// appsecrets.json (committed, demo values)
{
  "FileStorage": {
    "Encryption": {
      "ActiveKeyId": "local-dev",
      "Keys": { "local-dev": "4np4paalIZ479mKVwdxd61u5lXPXuhhO9zuZlHLvtMQ=" }
    }
  }
}
```

That demo key is public: it's committed. Anything beyond a local demo needs a key of your own,
generated with `openssl rand -base64 32` and kept safe in your secrets store or password manager. Losing
the key means losing every file it encrypted.

With it on, every file is encrypted with its own AES-256-GCM key, wrapped by the master key
(`GM.FileStorage.Encryption`), so the storage root holds only ciphertext. Storage doesn't learn what the file
is either: next to the bytes, the provider keeps only the name `encrypted` and the type
`application/octet-stream`; the real name, type and checksum are sealed under the file's key and restored on
download. Over HTTP nothing changes: uploads and downloads work as before.

Files stored while encryption was off stay as they were, readable through the API and on disk alike, until
they're uploaded again.

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/v1/files` | Upload one multipart `file`, streamed (several → 400, nothing kept); returns its `id` (a GUID), name, content type, size, checksum |
| `POST` | `/api/v1/files/batch` | Upload 1–20 multipart `files`, streamed; returns one result per file, in order: the stored file, or why it wasn't stored (21+ → 400, nothing kept) |
| `GET` | `/api/v1/files?userId=&contentType=&integrityProblem=&currentPage=&pageSize=` | Paged list of the tenant's files, uploader and last integrity check included |
| `GET` | `/api/v1/files/usage` | The tenant's storage use: used bytes, quota, file count |
| `GET` | `/api/v1/files/{id}` | File details (`?visibility=All` includes deleted files) |
| `GET` | `/api/v1/files/{id}/content` | Download the whole file (streamed); recorded in its history |
| `GET` | `/api/v1/files/{id}/history?action=&userId=&clientId=&sessionId=&ipAddress=&createdAtFrom=&createdAtTo=&orderBy=&currentPage=&pageSize=&lastKeyValue=` | What happened to the file and who did it (uploaded, downloaded, deleted, restored): filtered, ordered by any of its fields (newest first by default), paged by number or by key with GM.API paging (`X-Pagination` header) |
| `DELETE` | `/api/v1/files/{id}` | Mark the file deleted (the bytes are kept) |
| `POST` | `/api/v1/files/{id}/restore` | Restore a deleted file as it was |
| `GET` | `/health/live` / `/health/ready` / `/health/startup` | Liveness / readiness / startup probes (GM.API) |
| `GET` | `/` | Swagger UI |

`/api/v1` calls may carry `X-Tenant-Id` (a GUID) and `X-User-Id`; without a tenant they work in the shared
`public` space. Through the gateway, the same routes are on `https://localhost:7141`. Uploads are limited to
30 per minute per IP, everything else to 300; over the limit you get `429` with `Retry-After`.

## Database setup

The catalog lives in PostgreSQL, database `GMFileStorageSample`. To run the projects with `dotnet run`, start just
the database with Docker (to run everything in Docker instead, see [Running in Docker](#running-in-docker)):

```bash
docker compose up -d postgres   # postgres:16 on localhost:5432, user postgres / password 123456
```

The connection string lives in **`appsecrets.json`** of the API and of the worker, not in `appsettings.json`.
They're committed with local demo values only: the docker-compose database's credentials and a demo master
key. The cloud fields are empty. Put real keys in the secrets store named by `SECRETS_PROVIDER`, or in environment
variables (`FileStorage__S3__AccessKey`, …), not in these files, so they never reach git. Both files hold:

```json
{
  "ConnectionStrings": {
    "ApplicationDatabase": "Host=localhost;Port=5432;Database=GMFileStorageSample;Username=postgres;Password=123456"
  },
  "FileStorage": {
    "Encryption": {
      "ActiveKeyId": "local-dev",
      "Keys": { "local-dev": "4np4paalIZ479mKVwdxd61u5lXPXuhhO9zuZlHLvtMQ=" }
    },
    "S3": { "AccessKey": "", "SecretKey": "" },
    "AzureBlob": { "ConnectionString": "" }
  }
}
```

The worker reads its own copy. These read the API's:
- GM.API, which layers it over `appsettings.json` through GM.Secrets;
- the tests;
- the design-time factory that `dotnet ef` uses.

For a real database, put its connection string there (or in the secrets store named by `SECRETS_PROVIDER`),
never in a committed file.

The API applies pending migrations on startup, so the schema is created on first run. To add a migration:

```bash
dotnet ef migrations add <Name> -p GM.FileStorage.Sample.Persistence -s GM.FileStorage.Sample.Persistence -o Migrations
```

## Running

Start the three processes, each in its own terminal (the API first: it applies the migrations):

```bash
dotnet run --project GM.FileStorage.Sample.API          # https://localhost:7140
```

```bash
dotnet run --project GM.FileStorage.Sample.Worker       # storage cleanup, every minute
```

```bash
dotnet run --project GM.FileStorage.Sample.Gateway.API  # https://localhost:7141, Swagger UI at /
```

Then call the gateway, as a tenant and user (any GUIDs; leave `-H "$T"` out to use the shared space):

```bash
T="X-Tenant-Id: 3f2b9c1e-5d4a-4e7b-9a1c-2b8d6e0f4a71"
U="X-User-Id: 8d1e4c27-0b6a-4f3e-9c52-7a1f0e6b3d94"

curl -H "$T" -H "$U" -F "file=@passport.pdf" "https://localhost:7141/api/v1/files"
# { "id": "6f1e…-…", "fileName": "passport.pdf", "contentType": "application/pdf", "size": 12345, … }

curl -H "$T" -H "$U" -F "files=@visa.pdf" -F "files=@contract.docx" "https://localhost:7141/api/v1/files/batch"
# [ { "fileName": "visa.pdf", "succeeded": true, "file": { "id": "…", … } }, { "fileName": "contract.docx", … } ]

curl -H "$T" -H "$U" "https://localhost:7141/api/v1/files"
curl -H "$T" -H "$U" -o passport.pdf "https://localhost:7141/api/v1/files/6f1e…/content"
curl -H "$T" -H "$U" "https://localhost:7141/api/v1/files/6f1e…/history"
# [ { "action": "Downloaded", "occurredAtUtc": "…", "userId": "…", "ipAddress": "…", "userAgent": "curl/8.9.1", … }, { "action": "Uploaded", … } ]
curl -H "$T" -H "$U" -X DELETE "https://localhost:7141/api/v1/files/6f1e…"
curl -H "$T" -H "$U" -X POST "https://localhost:7141/api/v1/files/6f1e…/restore"
```

The API also works on its own for a quick try. Abandoned uploads then wait in the queue until a worker runs.

Files land under `FileStorage:Local:RootPath`, which is `C:/Files`, shared by the API and worker (Docker uses its
own volume instead). To run on S3/MinIO
or Azure Blob, set `FileStorage:Provider` to `"S3"` or `"AzureBlob"`; no code change is needed.
- **Non-secret settings** (bucket, region, container) go in `appsettings.json`.
- **Credentials** go in environment variables or the secrets store, or in `appsecrets.json` on your machine only
  (don't commit them):
  - `FileStorage:S3:AccessKey` / `SecretKey`;
  - `FileStorage:AzureBlob:ConnectionString`.

  Alternatively, keep them in the secrets store named by `SECRETS_PROVIDER`.

## Trying it with Bruno

[`bruno/`](bruno) is a [Bruno](https://www.usebruno.com) collection of every endpoint, through the gateway. Open the
folder in Bruno, pick the **Local** environment (gateway, API, tenant and user), and run **Files** in order: Upload
File saves the file's id for the rest. It uploads, retries with the same `Idempotency-Key`
(replayed), sends a batch and a refused type, then lists, reads, downloads, checks usage, tries the file as
another tenant (404), deletes, restores, and reads the file's history. **Health** checks both services. Every request
has tests, so the whole run doubles as a smoke test:

```bash
cd bruno
npx @usebruno/cli run Files Health --env Local --insecure   # --insecure: the local dev certificate
```

## Running in Docker

`docker-compose.yml` runs the whole sample: PostgreSQL, Redis, the API, two workers and the gateway, built from the
one `Dockerfile` (its `PROJECT` build argument picks the host). With Redis the rate limits, the per-client file count
and the job lock are shared, so the two workers never clean at the same time.

```bash
docker compose up -d --build
```

The gateway, and so Swagger UI, is on **http://localhost:8080**; the API isn't published, so every call goes through
the gateway's routes and rate limits, as in production. Inside the network the services talk plain HTTP. The
connection string comes from environment variables in the compose file instead of `appsecrets.json`, and the API and
worker share the `gm-filestorage-files` volume as their storage root. `docker compose down -v` removes everything,
volumes included.

To scan uploads for malware, add the ClamAV override. It runs ClamAV next to the stack and switches scanning on for
the API and the workers; the first start takes a minute or two while ClamAV downloads its signatures:

```bash
docker compose -f docker-compose.yml -f docker-compose.scanning.yml up -d --build
```

Outside Docker, run ClamAV yourself and set `FileStorage:MalwareScanner:Enabled` to `true` (and
`FileStorage:MalwareScanner:ClamAV:Host` / `Port` if it isn't on `localhost:3310`) for the API and the worker.

## Testing

```bash
dotnet test   # with Postgres running (see Database setup)
```

`FilesEndToEndTests` boot the API in-memory with `GmWebApplicationFactory<Program>` (GM.Testing.AspNetCore)
against a throwaway storage root and drive it over HTTP: upload (stored under its own id) → list by user → details →
download → delete (row marked deleted, bytes kept) → restore. They also cover batch uploads (one result
per file, a refused file not stopping the rest), the single endpoint refusing several files, Word documents, a
3 MB upload streamed intact, an oversized upload refused after reading little more than the limit, a `Range` header
ignored,
the upload rules, the cleanup job, integrity checks flagging damaged or
missing bytes and recording missing checksums, retention purging long-deleted files, the storage quota, the
per-client file count refusing a batch whole, one tenant never seeing or touching another's files, retries with an `Idempotency-Key` storing and counting a file once,
a file's history recording who downloaded, deleted and restored it (paged by number and by key, filtered, ordered),
and
`PUT` on a file's content being refused (405). They need PostgreSQL and
**no-op when it isn't reachable**; CI runs them against a `postgres` service container.
`AggregateConstructorTests` guard that every aggregate keeps a parameterless constructor for EF.

`GatewayTests` (`tests/GM.FileStorage.Sample.Gateway.Tests`) run the gateway as configured in its appsettings.json in
front of a stub API on a real port: which requests are forwarded (bodies included) and which aren't,
the 30-a-minute upload limit with `429` + `Retry-After` while reads keep their own limit, readiness following the
API, and Swagger UI served by the gateway itself. They need nothing running.

CI also builds the Docker images and runs `docker compose up`, then uploads, lists and downloads a file through the gateway, reads its history, and checks the API and worker
share the storage volume.

## License

MIT — see [LICENSE](LICENSE).
