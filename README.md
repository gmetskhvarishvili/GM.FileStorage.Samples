<p align="center">
  <img src="icon.png" alt="GM.FileStorage Samples" width="140" height="140" />
</p>

# GM.FileStorage Samples

[![CI](https://github.com/gmetskhvarishvili/GM.FileStorage.Samples/actions/workflows/ci.yml/badge.svg)](https://github.com/gmetskhvarishvili/GM.FileStorage.Samples/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A layered **DDD + CQRS** file-storage sample built on the **GM.\*** ecosystem: streaming upload/download,
a **tenant/user-scoped key strategy** (the KYC pattern), delete and presigned links through the
provider-agnostic `IFileStorageService` — plus a **PostgreSQL catalog** of every stored file and its
metadata, so files can be listed by tenant or user, inspected by id, soft-deleted and restored. Targets
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
work / domain-event store / actor auditing). The layout mirrors
[GM.Identity.Samples](https://github.com/gmetskhvarishvili/GM.Identity.Samples).

> The catalog packages (`GM.FileStorage.Domain` / `GM.FileStorage.Persistence`) ship in GM.FileStorage
> **1.2.0**; every `GM.FileStorage*` reference here is pinned to that version (they release in lockstep).

## Layers

| Project | Role |
| --- | --- |
| `GM.FileStorage.Sample.Domain` | `StoredFile` / `StoredFileMetadata` aggregates (specialising GM.FileStorage's `GMStoredFile` / `GMStoredFileMetadata`), repository interfaces, `StoredFileSpecification` (visibility, tenant/user metadata, key prefix, paging), `IUnitOfWork` |
| `GM.FileStorage.Sample.Application` | Commands and queries (`Files/Commands/UploadFile`, `DeleteFile`; `Files/Queries/GetFilesList`, `GetFileDetails`, `DownloadFile`, `GetFilePresignedUrl`) — request, FluentValidation validator and GM.Mediator handler in one file; Mapster `MappingRegister` |
| `GM.FileStorage.Sample.Persistence` | `ApplicationDbContext` (GM.EntityFramework `GenericDbContext` + domain-event dispatch), the GM.FileStorage configurations mapped to `file_storage.stored_files` / `stored_file_metadata` (`FileStorageTableNames`: the library's names without the `gm_` prefix), repositories, unit of work, migrations, `AddPersistence` (Npgsql + snake_case + actor auditing) |
| `GM.FileStorage.Sample.Infrastructure` | `AddInfrastructure`: GM.FileStorage + the Local/S3/AzureBlob providers, `TenantKeyStrategy`, the database readiness check |
| `GM.FileStorage.Sample.Common` | `StringResource` (display names, error parts) and the `GMExceptionsResource` messages GM.Exceptions formats |
| `GM.FileStorage.Sample.API` | GM.API host; `Files/FilesController` sends mediator requests and maps its `*Model`s with Mapster |
| `tests/GM.FileStorage.Sample.Tests` | End-to-end tests over HTTP (`GmWebApplicationFactory<Program>`) and aggregate guards |

## What it demonstrates

- **Bytes in storage, facts in the catalog.** `UploadFile` streams the request into `IFileStorageService`
  (tenant and user travel as upload metadata, which `TenantKeyStrategy` turns into
  `tenants/{tenantId}/users/{userId}/{guid}{ext}`), then records a `StoredFile` row plus one
  `StoredFileMetadata` row per pair and saves through the unit of work. If the save fails, the uploaded bytes
  are deleted and the error rethrown, so storage never keeps unreferenced objects.
- **One row per key.** Uploading to an existing key — even a deleted one — calls `StoredFile.Replace`, which
  restores the row, and syncs the metadata rows (changed, restored, added or soft-removed).
- **Soft delete.** `DeleteFile` soft-removes the row (kept as an audit trail, visible with `visibility=All`)
  and then deletes the bytes.
- **Searchable metadata.** The list filters by tenant/user with `Metadata.Any(...)` sub-queries served by the
  `(name, value)` index, plus key prefix and content type, with GM.API paging (`X-Pagination` header).
- **Domain events.** `GMStoredFileUploaded/Replaced/Deleted/RestoredDomainEvent`s are written to
  `domain_events.domain_events` in the same transaction and dispatched in-process after the save.
- **All three backends wired at once** — `FileStorage:Provider` (default `"Local"`) chooses which one is
  active, so switching disk → S3 → Azure is a **config change, not a code change**. The cloud clients are
  only constructed when their provider is selected.
- **GM.API hosting** — Swagger UI at `/` (styled by `Assets/`), health checks at `/health/live|ready|startup`
  (ready includes the database), request logging that describes upload/download bodies instead of reading
  them, RFC 7807 errors (`NotFoundException` → 404), and secrets layered into configuration.

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/v1/files?tenantId=&userId=&key=` | Upload a multipart `file`; returns id, key, size, checksum. `key` (optional) uploads to an explicit key and replaces/restores it |
| `GET` | `/api/v1/files?tenantId=&userId=&keyPrefix=&contentType=&currentPage=&pageSize=` | Paged list of catalogued files, metadata included |
| `GET` | `/api/v1/files/{id}` | File details by catalog id (`?visibility=All` includes deleted files) |
| `GET` | `/api/v1/files/{**key}` | Download the file (streamed) |
| `DELETE` | `/api/v1/files/{**key}` | Delete the bytes and soft-delete the row |
| `GET` | `/api/v1/presign/{**key}` | A time-limited link (needs `FileStorage:Local:PublicBaseUrl` for the local provider) |
| `GET` | `/health/live` / `/health/ready` / `/health/startup` | Liveness / readiness / startup probes (GM.API) |
| `GET` | `/` | Swagger UI |

## Database setup

The catalog lives in PostgreSQL, database `GMFileStorageSample`. Start one with Docker:

```bash
docker compose up -d   # postgres:16 on localhost:5432, user postgres / password 123456
```

The committed `appsettings.json` has the connection string **without a password**
(`Host=localhost;Port=5432;Database=GMFileStorageSample;Username=postgres`). Supply the real one in either of
two ways — never commit it:

- an environment variable:

  ```bash
  export ConnectionStrings__ApplicationDatabase="Host=localhost;Port=5432;Database=GMFileStorageSample;Username=postgres;Password=123456"
  ```

- or a gitignored `GM.FileStorage.Sample.API/appsecrets.json` (GM.API layers it into configuration; the
  design-time factory used by `dotnet ef` reads it too):

  ```json
  { "ConnectionStrings": { "ApplicationDatabase": "Host=localhost;Port=5432;Database=GMFileStorageSample;Username=postgres;Password=123456" } }
  ```

The API applies pending migrations on startup, so the schema is created on first run. To add a migration:

```bash
dotnet ef migrations add <Name> -p GM.FileStorage.Sample.Persistence -s GM.FileStorage.Sample.Persistence -o Migrations
```

## Running

```bash
dotnet run --project GM.FileStorage.Sample.API

curl -F "file=@passport.pdf" "http://localhost:5xxx/api/v1/files?tenantId=acme&userId=u1"
# { "id": "…", "key": "tenants/acme/users/u1/9f1c…a3.pdf", "fileName": "passport.pdf", "size": 12345, … }

curl "http://localhost:5xxx/api/v1/files?tenantId=acme"
curl -O "http://localhost:5xxx/api/v1/files/tenants/acme/users/u1/9f1c…a3.pdf"
curl -X DELETE "http://localhost:5xxx/api/v1/files/tenants/acme/users/u1/9f1c…a3.pdf"
```

Files land under `FileStorage:Local:RootPath` (default `App_Data/file-storage`, gitignored). To run on S3/MinIO
or Azure Blob, fill in `FileStorage:S3` / `FileStorage:AzureBlob` and set `FileStorage:Provider` to `"S3"` or
`"AzureBlob"` — no code change. Keep `FileStorage:S3:SecretKey` and `FileStorage:AzureBlob:ConnectionString`
out of `appsettings.json`: GM.API reads them from `appsecrets.json` or the secrets store named by
`SECRETS_PROVIDER`.

## Testing

```bash
ConnectionStrings__ApplicationDatabase="Host=localhost;Port=5432;Database=GMFileStorageSample;Username=postgres;Password=123456" dotnet test
```

`FilesEndToEndTests` boot the API in-memory with `GmWebApplicationFactory<Program>` (GM.Testing.AspNetCore)
against a throwaway storage root and drive it over HTTP: upload → list by tenant/user/key prefix → details →
download → presign → delete (row soft-deleted, bytes gone) → re-upload to the same key restores the row. They
need PostgreSQL and **no-op when it isn't reachable**; CI runs them against a `postgres` service container.
`AggregateConstructorTests` guard that every aggregate keeps a parameterless constructor for EF.

## License

MIT — see [LICENSE](LICENSE).
