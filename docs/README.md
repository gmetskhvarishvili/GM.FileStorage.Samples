# Diagrams

Every diagram is named **subject — kind**. The name is both its heading and the title drawn on the diagram, so
the same words find it in the docs, in a search, or in an exported image.

## By action

Each action has a user flow (what the caller sees) and a sequence (what happens inside).

| Action | Endpoint | User flow | Sequence |
| --- | --- | --- | --- |
| Gateway | every request | [Gateway checks — user flow](user-flows.md#gateway-checks--user-flow) | [Gateway forwarding — sequence](sequence-diagrams.md#gateway-forwarding--sequence) |
| Upload | `POST /api/v1/files` | [Upload — user flow](user-flows.md#upload--user-flow) | [Upload — sequence](sequence-diagrams.md#upload--sequence) |
| Batch upload | `POST /api/v1/files/batch` | [Batch upload — user flow](user-flows.md#batch-upload--user-flow) | [Batch upload — sequence](sequence-diagrams.md#batch-upload--sequence) |
| List | `GET /api/v1/files` | [List — user flow](user-flows.md#list--user-flow) | [List — sequence](sequence-diagrams.md#list--sequence) |
| Details | `GET /api/v1/files/{id}` | [Details — user flow](user-flows.md#details--user-flow) | [Details — sequence](sequence-diagrams.md#details--sequence) |
| Download | `GET /api/v1/files/{id}/content` | [Download — user flow](user-flows.md#download--user-flow) | [Download — sequence](sequence-diagrams.md#download--sequence) |
| Delete | `DELETE /api/v1/files/{id}` | [Delete — user flow](user-flows.md#delete--user-flow) | [Delete — sequence](sequence-diagrams.md#delete--sequence) |
| Restore | `POST /api/v1/files/{id}/restore` | [Restore — user flow](user-flows.md#restore--user-flow) | [Restore — sequence](sequence-diagrams.md#restore--sequence) |

## By doc

### [Architecture](architecture.md)

| Diagram | Shows |
| --- | --- |
| [System architecture — flowchart](architecture.md#system-architecture--flowchart) | Gateway, API and worker, and what each talks to |
| [Layers — dependency graph](architecture.md#layers--dependency-graph) | Which layer each host uses |
| [Data model — ER diagram](architecture.md#data-model--er-diagram) | `stored_files` (with `tenant_id`), `storage_cleanups` |

### [Dependencies](dependencies.md)

| Diagram | Shows |
| --- | --- |
| [Runtime dependencies — flowchart](dependencies.md#runtime-dependencies--flowchart) | What each process calls at run time: PostgreSQL, storage, secrets |
| [Project references — dependency graph](dependencies.md#project-references--dependency-graph) | Which sample project references which |
| [Package dependencies — dependency graph](dependencies.md#package-dependencies--dependency-graph) | The GM packages each project uses |
| [GM.FileStorage packages — dependency graph](dependencies.md#gmfilestorage-packages--dependency-graph) | How the GM.FileStorage packages depend on each other |
| [Request classes — class diagram](dependencies.md#request-classes--class-diagram) | Controller → mediator → handlers → unit of work, storage, key strategy |
| [Domain model — class diagram](dependencies.md#domain-model--class-diagram) | The aggregates, their GM base classes and the repositories |

### [User flows](user-flows.md)

| Diagram | Shows |
| --- | --- |
| [File lifecycle — state diagram](user-flows.md#file-lifecycle--state-diagram) | Live, deleted, restored, purged by retention; content never changes |
| [Gateway checks — user flow](user-flows.md#gateway-checks--user-flow) | Route match, rate limits, 429 / 502 |
| [Upload — user flow](user-flows.md#upload--user-flow) | One file streamed, validation, upload rules, 200 / 400 |
| [Batch upload — user flow](user-flows.md#batch-upload--user-flow) | 1–20 files, one result each, retry the failed ones; 21+ discards and refuses |
| [List — user flow](user-flows.md#list--user-flow) | Filters and paging |
| [Details — user flow](user-flows.md#details--user-flow) | Visible or deleted file, 200 / 404 |
| [Download — user flow](user-flows.md#download--user-flow) | Whole file or a range, 200 / 206 / 404 / 416 |
| [Delete — user flow](user-flows.md#delete--user-flow) | Soft delete, 204 / 404 |
| [Restore — user flow](user-flows.md#restore--user-flow) | Bytes still there, 204 / 404 |
| [Typical session — user journey](user-flows.md#typical-session--user-journey) | One document from upload to restore |

### [Sequence diagrams](sequence-diagrams.md)

| Diagram | Shows |
| --- | --- |
| [Startup — sequence](sequence-diagrams.md#startup--sequence) | Secrets, migrations, job scheduling, the gateway's routes |
| [Gateway forwarding — sequence](sequence-diagrams.md#gateway-forwarding--sequence) | Rate-limit check, forwarding, 429 / 502 |
| [Upload — sequence](sequence-diagrams.md#upload--sequence) | Streamed off the request, queue, upload, one transaction, failures, discard |
| [Batch upload — sequence](sequence-diagrams.md#batch-upload--sequence) | One single upload per file as it arrives, per-file results, discard past 20 |
| [List — sequence](sequence-diagrams.md#list--sequence) | Specification, count, page, mapping |
| [Details — sequence](sequence-diagrams.md#details--sequence) | Visibility filter |
| [Download — sequence](sequence-diagrams.md#download--sequence) | Ranges, missing bytes, 206 / 416 |
| [Delete — sequence](sequence-diagrams.md#delete--sequence) | Soft remove, bytes untouched |
| [Restore — sequence](sequence-diagrams.md#restore--sequence) | Bytes check, restore |
| [Encryption at rest — sequence](sequence-diagrams.md#encryption-at-rest--sequence) | Per-file key, chunked AES-GCM, ranged decryption |
| [Storage cleanup — sequence](sequence-diagrams.md#storage-cleanup--sequence) | The worker's lock, claim, delete, back-off |
| [Integrity check — sequence](sequence-diagrams.md#integrity-check--sequence) | Reading stored bytes back: missing, wrong size, checksum mismatch, checksums filled in |
| [Retention — sequence](sequence-diagrams.md#retention--sequence) | Purging files deleted longer ago than retention allows |
| [Health checks — sequence](sequence-diagrams.md#health-checks--sequence) | Gateway and API liveness and readiness |
| [Swagger via gateway — sequence](sequence-diagrams.md#swagger-via-gateway--sequence) | UI from the gateway, document from the API |

### [Configuration](configuration.md)

| Diagram | Shows |
| --- | --- |
| [Configuration sources — flowchart](configuration.md#configuration-sources--flowchart) | appsettings, appsecrets, secrets store and environment layered into configuration |

### [Main README](../README.md)

| Diagram | Shows |
| --- | --- |
| [System overview — flowchart](../README.md#gmfilestorage-samples) | The three processes at a glance |
