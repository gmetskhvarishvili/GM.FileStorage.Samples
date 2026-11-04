# Dependencies

Four views, from the outside in: what each process talks to at run time, how the projects reference each other,
which packages each project pulls in, and how the classes inside a request depend on each other.

**Diagrams in this doc:** [Runtime dependencies](#runtime-dependencies--flowchart) · [Project references](#project-references--dependency-graph) · [Package dependencies](#package-dependencies--dependency-graph) · [GM.FileStorage packages](#gmfilestorage-packages--dependency-graph) · [Request classes](#request-classes--class-diagram) · [Domain model](#domain-model--class-diagram)  
All diagrams: [index](README.md)

## Runtime dependencies — flowchart

```mermaid
---
title: Runtime dependencies — flowchart
---
flowchart LR
    client([Client / browser])

    subgraph processes[Sample processes]
        gw[Gateway.API<br/>:7141]
        api[Files API<br/>:7140]
        worker[Worker]
    end

    pg[(PostgreSQL<br/>GMFileStorageSample)]
    store[(Storage provider<br/>Local folder · S3 · Azure Blob)]
    secrets[[Secrets<br/>appsecrets.json · SECRETS_PROVIDER · env]]

    client -->|HTTPS: /api/v1/files/**, /swagger/**| gw
    gw -->|forwards requests| api
    gw -->|readiness: GET /health/live| api
    api -->|catalog, cleanup queue, domain events| pg
    api -->|upload, download, exists, delete, presign| store
    worker -->|cleanup queue, file rows| pg
    worker -->|delete| store
    secrets -.->|connection string, cloud credentials, master keys| api
    secrets -.->|connection string, cloud credentials| worker
```

| Process | Needs at startup | Needs per request / run |
| --- | --- | --- |
| Gateway | The API's address | The API |
| Files API | PostgreSQL (applies migrations), secrets | PostgreSQL, storage |
| Worker | Secrets | PostgreSQL, storage |

## Project references — dependency graph

```mermaid
---
title: Project references — dependency graph
---
flowchart TB
    subgraph hosts[Hosts]
        gw[Gateway.API]
        api[API]
        worker[Worker]
    end
    infra[Infrastructure]
    pers[Persistence]
    app[Application]
    domain[Domain]
    common[Common]
    tests[[Tests]]

    api --> app
    api --> infra
    api --> pers
    worker --> infra
    worker --> pers
    infra --> app
    infra --> domain
    infra --> pers
    pers --> app
    pers --> domain
    app --> domain
    app --> common
    tests --> api
    tests --> domain
```

- **Domain and Common reference no other sample project.** Everything points inward to them.
- **Application knows only the domain**, through `IUnitOfWork` and the repository interfaces, plus
  `IFileStorageService` from GM.FileStorage. It never sees EF Core or a storage provider.
- **Gateway.API references no sample project.** It only knows the API's URL from configuration.
- **The worker is the API without HTTP:** it shares Persistence and Infrastructure and registers its own subset
  (`AddWorkerInfrastructure`).

## Package dependencies — dependency graph

```mermaid
---
title: Package dependencies — dependency graph
---
flowchart LR
    subgraph sample[Sample projects]
        gw[Gateway.API]
        api[API]
        worker[Worker]
        infra[Infrastructure]
        pers[Persistence]
        app[Application]
        domain[Domain]
        common[Common]
    end

    subgraph gmfs[GM.FileStorage 2.0.2]
        fs[GM.FileStorage]
        fsDomain[GM.FileStorage.Domain]
        fsPers[GM.FileStorage.Persistence]
        fsLocal[GM.FileStorage.Local]
        fsS3[GM.FileStorage.S3]
        fsAzure[GM.FileStorage.AzureBlob]
        fsEnc[GM.FileStorage.Encryption]
    end

    subgraph gm[Other GM packages]
        gmApi[GM.API · GM.API.Application]
        gmMed[GM.Mediator]
        gmEf[GM.EntityFramework.Domain · .Persistence]
        gmExc[GM.Exceptions]
        gmGw[GM.Gateway]
        gmCache[GM.Caching]
        gmLock[GM.DistributedLock]
        gmHc[GM.HealthChecks · .EntityFramework]
        gmSched[GM.Scheduling]
        gmSec[GM.Secrets]
    end

    gw --> gmGw & gmCache & gmLock & gmHc
    api --> gmApi & gmMed
    worker --> gmSec
    infra --> fs & fsLocal & fsS3 & fsAzure & fsEnc & gmHc & gmSched & gmLock
    pers --> fsPers & gmEf
    app --> fs & gmApi & gmMed
    domain --> fsDomain & gmEf
    common --> gmExc
```

Third-party packages, by project:

| Project | Packages |
| --- | --- |
| Gateway.API | Swashbuckle.AspNetCore.SwaggerUI (UI only; the document comes from the API) |
| API | Asp.Versioning.Mvc, FluentValidation, Mapster, Microsoft.EntityFrameworkCore.Design |
| Application | Mapster |
| Persistence | Npgsql.EntityFrameworkCore.PostgreSQL, EFCore.NamingConventions (snake_case), EF Core Design/Tools |
| Tests | xunit, GM.Testing, GM.Testing.AspNetCore |

## GM.FileStorage packages — dependency graph

```mermaid
---
title: GM.FileStorage packages — dependency graph
---
flowchart BT
    fs[GM.FileStorage<br/>IFileStorageService, key templates, upload rules, ranges]
    local[GM.FileStorage.Local] --> fs
    s3[GM.FileStorage.S3] --> fs
    azure[GM.FileStorage.AzureBlob] --> fs
    enc[GM.FileStorage.Encryption<br/>decorates IFileStorageService] --> fs
    s3 --> aws[AWSSDK.S3]
    azure --> blobs[Azure.Storage.Blobs]

    fsDomain[GM.FileStorage.Domain<br/>GMStoredFile] --> efDomain[GM.EntityFramework.Domain]
    fsPers[GM.FileStorage.Persistence<br/>EF configurations] --> fsDomain
    fsPers --> efPers[GM.EntityFramework.Persistence]
```

The storage half (`GM.FileStorage` and its providers) and the catalog half (`Domain` / `Persistence`) don't
reference each other. The sample is what joins them: a handler writes bytes through `IFileStorageService` and
records them through the unit of work.

## Request classes — class diagram

```mermaid
---
title: Request classes — class diagram
---
classDiagram
    direction LR

    class FilesController {
        +UploadFile()
        +UploadFiles()
        +GetFilesList()
        +GetFileDetails()
        +DownloadFile()
        +DeleteFile()
        +RestoreFile()
        +PresignFile()
    }
    class IMediator
    class UploadFileCommandHandler
    class UploadFilesCommandHandler
    class DiscardFilesCommandHandler
    class MultipartFiles {
        +ReadAsync(request) file parts
    }
    class DeleteFileCommandHandler
    class RestoreFileCommandHandler
    class GetFilesListQueryHandler
    class GetFileDetailsQueryHandler
    class DownloadFileQueryHandler
    class GetFilePresignedUrlQueryHandler

    class IUnitOfWork {
        +StoredFileRepository
        +StorageCleanupRepository
        +SaveChangesAsync()
    }
    class IFileStorageService {
        +UploadAsync()
        +DownloadAsync()
        +ExistsAsync()
        +DeleteAsync()
        +GetPresignedUrlAsync()
    }
    class IFileKeyStrategy {
        +GenerateKey()
    }
    class StorageCleanupJob

    FilesController --> IMediator
    FilesController --> MultipartFiles : uploads, read off the request
    IMediator ..> UploadFileCommandHandler
    IMediator ..> UploadFilesCommandHandler
    IMediator ..> DiscardFilesCommandHandler
    UploadFilesCommandHandler --> IMediator : one UploadFileCommand per file
    DiscardFilesCommandHandler --> IUnitOfWork
    DiscardFilesCommandHandler --> IFileStorageService
    IMediator ..> DeleteFileCommandHandler
    IMediator ..> RestoreFileCommandHandler
    IMediator ..> GetFilesListQueryHandler
    IMediator ..> GetFileDetailsQueryHandler
    IMediator ..> DownloadFileQueryHandler
    IMediator ..> GetFilePresignedUrlQueryHandler

    UploadFileCommandHandler --> IUnitOfWork
    UploadFileCommandHandler --> IFileStorageService
    UploadFileCommandHandler --> IFileKeyStrategy
    DeleteFileCommandHandler --> IUnitOfWork
    RestoreFileCommandHandler --> IUnitOfWork
    RestoreFileCommandHandler --> IFileStorageService
    GetFilesListQueryHandler --> IUnitOfWork
    GetFileDetailsQueryHandler --> IUnitOfWork
    DownloadFileQueryHandler --> IUnitOfWork
    DownloadFileQueryHandler --> IFileStorageService
    GetFilePresignedUrlQueryHandler --> IUnitOfWork
    GetFilePresignedUrlQueryHandler --> IFileStorageService
    StorageCleanupJob --> IUnitOfWork
    StorageCleanupJob --> IFileStorageService
```

Delete, list and details touch only the catalog. Restore asks storage too, to check the bytes are still there.
The batch handler touches neither: it sends one `UploadFileCommand` per file back through the mediator, so each
file gets the same validation, rules and ordering as a single upload. Uploads never bind an `IFormFile`: the
controller reads file parts straight off the request with `MultipartFiles`, and `DiscardFilesCommand` (not exposed
over HTTP) undoes files stored by a request that's then refused.

## Domain model — class diagram

```mermaid
---
title: Domain model — class diagram
---
classDiagram
    direction TB

    class GMStoredFile~TFile~ {
        <<GM.FileStorage.Domain>>
        +Guid Id
        +string StorageKey
        +string FileName
        +string ContentType
        +long Size
        +string Checksum
        +bool IsPublic
        +SoftRemove()
        +RestoreAll()
    }
    class StoredFile {
        +string TenantId
        +Create(id, storageKey, …, tenantId)$
    }
    class StorageCleanup {
        +string StorageKey
        +DateTime DueAtUtc
        +int Attempts
        +Create(storageKey, dueAtUtc)$
        +Claim(nowUtc)
    }
    class IUnitOfWork {
        <<interface>>
    }
    class IStoredFileRepository {
        <<interface>>
    }
    class IStorageCleanupRepository {
        <<interface>>
    }

    GMStoredFile <|-- StoredFile
    IUnitOfWork --> IStoredFileRepository
    IUnitOfWork --> IStorageCleanupRepository
    IStoredFileRepository ..> StoredFile
    IStorageCleanupRepository ..> StorageCleanup
```

`StorageCleanup` names a storage key, not a file, so it has no link to `StoredFile`. The job checks at run time
whether any file still uses the key.
