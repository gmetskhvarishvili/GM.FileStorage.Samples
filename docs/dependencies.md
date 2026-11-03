# Dependencies

Four views, from the outside in: what each process talks to at run time, how the projects reference each other,
which packages each project pulls in, and how the classes inside a request depend on each other.

## Runtime: processes and what they call

```mermaid
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

## Project references

```mermaid
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

## Packages per project

```mermaid
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

    subgraph gmfs[GM.FileStorage 1.3.0]
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

### Inside GM.FileStorage

```mermaid
flowchart BT
    fs[GM.FileStorage<br/>IFileStorageService, key templates, upload rules, ranges]
    local[GM.FileStorage.Local] --> fs
    s3[GM.FileStorage.S3] --> fs
    azure[GM.FileStorage.AzureBlob] --> fs
    enc[GM.FileStorage.Encryption<br/>decorates IFileStorageService] --> fs
    s3 --> aws[AWSSDK.S3]
    azure --> blobs[Azure.Storage.Blobs]

    fsDomain[GM.FileStorage.Domain<br/>GMStoredFile, GMStoredFileMetadata] --> efDomain[GM.EntityFramework.Domain]
    fsPers[GM.FileStorage.Persistence<br/>EF configurations] --> fsDomain
    fsPers --> efPers[GM.EntityFramework.Persistence]
```

The storage half (`GM.FileStorage` and its providers) and the catalog half (`Domain` / `Persistence`) don't
reference each other. The sample is what joins them: a handler writes bytes through `IFileStorageService` and
records them through the unit of work.

## Classes behind a request

```mermaid
classDiagram
    direction LR

    class FilesController {
        +UploadFile()
        +ReplaceFileContent()
        +GetFilesList()
        +GetFileDetails()
        +DownloadFile()
        +DeleteFile()
        +RestoreFile()
        +PresignFile()
    }
    class IMediator
    class UploadFileCommandHandler
    class DeleteFileCommandHandler
    class RestoreFileCommandHandler
    class GetFilesListQueryHandler
    class GetFileDetailsQueryHandler
    class DownloadFileQueryHandler
    class GetFilePresignedUrlQueryHandler

    class IUnitOfWork {
        +StoredFileRepository
        +StoredFileMetadataRepository
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
    IMediator ..> UploadFileCommandHandler
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

### Domain model

```mermaid
classDiagram
    direction TB

    class GMStoredFile~TFile,TMetadata~ {
        <<GM.FileStorage.Domain>>
        +Guid Id
        +string Key
        +string StorageKey
        +string FileName
        +string ContentType
        +long Size
        +string Checksum
        +bool IsPublic
        +Replace() previous storage key
        +SoftRemove()
        +RestoreAll()
    }
    class GMStoredFileMetadata~TFile,TMetadata~ {
        <<GM.FileStorage.Domain>>
        +Guid StoredFileId
        +string Name
        +string Value
        +ChangeValue()
    }
    class StoredFile {
        +Create(id, storageKey, …)$
    }
    class StoredFileMetadata {
        +Create(storedFileId, name, value)$
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
    class IStoredFileMetadataRepository {
        <<interface>>
    }
    class IStorageCleanupRepository {
        <<interface>>
    }

    GMStoredFile <|-- StoredFile
    GMStoredFileMetadata <|-- StoredFileMetadata
    StoredFile "1" o-- "*" StoredFileMetadata : Metadata
    IUnitOfWork --> IStoredFileRepository
    IUnitOfWork --> IStoredFileMetadataRepository
    IUnitOfWork --> IStorageCleanupRepository
    IStoredFileRepository ..> StoredFile
    IStoredFileMetadataRepository ..> StoredFileMetadata
    IStorageCleanupRepository ..> StorageCleanup
```

`StorageCleanup` names a storage key, not a file, so it has no link to `StoredFile`. The job checks at run time
whether any file still uses the key.
