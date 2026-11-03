# User flows

What a caller can do with a file, and every outcome they can get back. Each flow starts at the gateway
(`https://localhost:7141`); the [sequence diagrams](sequence-diagrams.md) show what happens inside.

**Diagrams in this doc:** [File lifecycle](#file-lifecycle--state-diagram) · [Gateway checks](#gateway-checks--user-flow) · [Upload](#upload--user-flow) · [Batch upload](#batch-upload--user-flow) · [List](#list--user-flow) · [Details](#details--user-flow) · [Download](#download--user-flow) · [Delete](#delete--user-flow) · [Restore](#restore--user-flow) · [Presign](#presign--user-flow) · [Typical session](#typical-session--user-journey)  
All diagrams: [index](README.md)

## File lifecycle — state diagram

```mermaid
---
title: File lifecycle — state diagram
---
stateDiagram-v2
    direction LR
    [*] --> Live: upload (POST /files)
    Live --> Deleted: delete (DELETE /files/{id})<br/>bytes kept
    Deleted --> Live: restore (POST /files/{id}/restore)

    state Live {
        direction LR
        [*] --> Listed
        Listed: listed, details, download, presign
    }
    state Deleted {
        direction LR
        [*] --> Hidden
        Hidden: details with visibility=All only
    }
```

A file's content never changes after upload: there is no replace, and new content is a new file with a new id.
A file is never removed from the catalog through the API, and its bytes are never deleted. Only the bytes of
uploads that were never recorded are deleted, by the worker.

## Gateway checks — user flow

```mermaid
---
title: Gateway checks — user flow
---
flowchart TD
    req([Request to :7141]) --> route{Matches a route?}
    route -- no --> nf404[404 Not Found]
    route -- yes --> policy{Which policy?}
    policy -- POST /files or /files/batch --> up{Under 30 uploads<br/>this minute for this IP?}
    policy -- other /files routes --> api{Under 300 requests<br/>this minute for this IP?}
    policy -- /swagger/** --> fwd
    up -- no --> r429[429 Too Many Requests<br/>+ Retry-After]
    api -- no --> r429
    up -- yes --> fwd[Forward to the files API<br/>+ X-RateLimit-* headers]
    api -- yes --> fwd
    fwd --> down{API reachable?}
    down -- no --> r502[502 Bad Gateway]
    down -- yes --> flows([The flows below])
```

Bodies over 60 MB are refused (`413`) by the gateway and by the API. A batch counts as one upload, however many
files it holds.

## Upload — user flow

```mermaid
---
title: Upload — user flow
---
flowchart TD
    start([POST /api/v1/files<br/>multipart file, tenantId?, userId?]) --> one{Exactly one file?}
    one -- no, several --> m400[400 use /files/batch<br/>nothing stored]
    one -- yes --> valid{File name, content type<br/>and ids valid?}
    valid -- no --> v400[400 validation errors]
    valid -- yes --> rules{Upload rules pass?<br/>size · allowed type · bytes match type}
    rules -- no --> r400[400 with the reason<br/>nothing kept]
    rules -- yes --> stored[Bytes stored at<br/>category/shard/id.ext]
    stored --> saved{Catalog saved?}
    saved -- no --> e500[500<br/>bytes deleted or queued]
    saved -- yes --> ok[200 id, storageKey, fileName,<br/>contentType, size, checksum]
    ok --> next([Keep the id: every other call uses it])
```

Without `tenantId` / `userId` the file is recorded under `public` / `anon`.

## Batch upload — user flow

```mermaid
---
title: Batch upload — user flow
---
flowchart TD
    start([POST /api/v1/files/batch<br/>multipart files, tenantId?, userId?]) --> count{1 to 20 files?}
    count -- no --> c400[400<br/>nothing stored]
    count -- yes --> each[[For each file, in order]]
    each --> one{Stored as<br/>POST /files would?}
    one -- yes --> okItem[succeeded: true<br/>file: id, storageKey, …]
    one -- "no: validation or upload rule" --> badItem[succeeded: false<br/>error: the reason]
    one -- "no: server fault" --> errItem[succeeded: false<br/>error: try again]
    okItem --> more{More files?}
    badItem --> more
    errItem --> more
    more -- yes --> each
    more -- no --> ok[200 one result per file,<br/>in the order sent]
    ok --> retry([Resend only the failed ones])
```

Each file is a file of its own, with its own id, stored exactly as a single upload. One bad file doesn't stop the
others, so the response is `200` even when some (or all) files failed: check `succeeded` on each result. The whole
request is still capped at 60 MB.

## List — user flow

```mermaid
---
title: List — user flow
---
flowchart TD
    start([GET /api/v1/files]) --> filters[Optional filters:<br/>tenantId · userId · folder · contentType<br/>dates · visibility]
    filters --> paging[Optional paging:<br/>currentPage · pageSize · ordering]
    paging --> res[200 page of files<br/>+ X-Pagination header]
    res --> more{More pages?}
    more -- yes --> paging
    more -- no --> done([Pick a file's id])
```

Deleted files are left out unless `visibility` includes them. An empty page is still `200`.

## Details — user flow

```mermaid
---
title: Details — user flow
---
flowchart TD
    start([GET /api/v1/files/id]) --> guid{id is a GUID?}
    guid -- no --> n404[404 no such route]
    guid -- yes --> exists{File exists and is visible?<br/>deleted only with visibility=All}
    exists -- no --> f404[404 StoredFile not found]
    exists -- yes --> ok[200 details:<br/>storageKey, size, checksum,<br/>metadata, audit dates]
```

## Download — user flow

```mermaid
---
title: Download — user flow
---
flowchart TD
    start([GET /api/v1/files/id/content]) --> live{Live file?}
    live -- no, unknown or deleted --> f404[404]
    live -- yes --> bytes{Bytes in storage?}
    bytes -- no, removed out of band --> b404[404]
    bytes -- yes --> range{Range header?}
    range -- none, or several ranges --> full[200 whole file, streamed<br/>Accept-Ranges: bytes]
    range -- one range --> fits{Inside the file?}
    fits -- no --> r416[416 Range Not Satisfiable<br/>Content-Range: bytes */size]
    fits -- yes --> part[206 Partial Content<br/>only those bytes read]
```

Ranges let video players seek and interrupted downloads resume. With encryption at rest on, the same responses
come back decrypted.

## Delete — user flow

```mermaid
---
title: Delete — user flow
---
flowchart TD
    start([DELETE /api/v1/files/id]) --> live{Live file?}
    live -- no, unknown or already deleted --> f404[404]
    live -- yes --> ok[204 No Content]
    ok --> after([Hidden from list, download and presign<br/>bytes kept, restorable any time])
```

## Restore — user flow

```mermaid
---
title: Restore — user flow
---
flowchart TD
    start([POST /api/v1/files/id/restore]) --> deleted{A deleted file?}
    deleted -- no, unknown or live --> f404[404]
    deleted -- yes --> bytes{Its bytes still in storage?}
    bytes -- no --> b404[404]
    bytes -- yes --> ok[204 No Content]
    ok --> after([Back as it was:<br/>same content, same metadata])
```

## Presign — user flow

```mermaid
---
title: Presign — user flow
---
flowchart TD
    start([GET /api/v1/files/id/presign]) --> live{Live file?}
    live -- no --> f404[404]
    live -- yes --> can{Can the storage hand out<br/>direct links?}
    can -- "no: encryption on, or Local<br/>without PublicBaseUrl" --> r400[400 with the reason<br/>download through the API instead]
    can -- "yes: S3, Azure, Local with PublicBaseUrl" --> ok[200 url, expiresAtUtc]
    ok --> use([Anyone with the link fetches the bytes<br/>straight from storage until it expires])
```

A file's content never changes, so a link always serves the bytes that were uploaded.

## Typical session — user journey

```mermaid
journey
    title Typical session — user journey
    section Store
      Upload passport.pdf: 5: Caller
      Upload the other scans in one batch: 4: Caller
      Find it by tenant: 4: Caller
      Check its details: 4: Caller
    section Use
      Download it: 5: Caller
      Resume a download with Range: 4: Caller
      Share a time-limited link: 3: Caller
    section Change
      Upload a newer scan as a new file: 4: Caller
      Delete it by mistake: 2: Caller
      Restore it: 5: Caller
```
