# User flows

What a caller can do with a file, and every outcome they can get back. Each flow starts at the gateway
(`https://localhost:7141`); the [sequence diagrams](sequence-diagrams.md) show what happens inside.

## The file's lifecycle

```mermaid
stateDiagram-v2
    direction LR
    [*] --> Live: upload (POST /files)
    Live --> Live: replace content (PUT /files/{id}/content)<br/>same id, new bytes
    Live --> Deleted: delete (DELETE /files/{id})<br/>bytes kept
    Deleted --> Live: restore (POST /files/{id}/restore)
    Deleted --> Live: replace content<br/>new bytes, restored

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

A file is never removed from the catalog through the API, and its current bytes are never deleted. Only the
bytes of previous versions and of uploads that were never recorded are deleted, by the worker.

## Every request: the gateway first

```mermaid
flowchart TD
    req([Request to :7141]) --> route{Matches a route?}
    route -- no --> nf404[404 Not Found]
    route -- yes --> policy{Which policy?}
    policy -- POST /files or PUT …/content --> up{Under 30 uploads<br/>this minute for this IP?}
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

Bodies over 60 MB are refused (`413`) by the gateway and by the API.

## Upload a file

```mermaid
flowchart TD
    start([POST /api/v1/files<br/>multipart file, tenantId?, userId?]) --> valid{File name, content type<br/>and ids valid?}
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

## Find files

```mermaid
flowchart TD
    start([GET /api/v1/files]) --> filters[Optional filters:<br/>tenantId · userId · folder · contentType<br/>dates · visibility]
    filters --> paging[Optional paging:<br/>currentPage · pageSize · ordering]
    paging --> res[200 page of files<br/>+ X-Pagination header]
    res --> more{More pages?}
    more -- yes --> paging
    more -- no --> done([Pick a file's id])
```

Deleted files are left out unless `visibility` includes them. An empty page is still `200`.

## View a file's details

```mermaid
flowchart TD
    start([GET /api/v1/files/id]) --> guid{id is a GUID?}
    guid -- no --> n404[404 no such route]
    guid -- yes --> exists{File exists and is visible?<br/>deleted only with visibility=All}
    exists -- no --> f404[404 StoredFile not found]
    exists -- yes --> ok[200 details:<br/>storageKey, size, checksum,<br/>metadata, audit dates]
```

## Download a file

```mermaid
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

## Replace a file's content

```mermaid
flowchart TD
    start([PUT /api/v1/files/id/content<br/>multipart file, tenantId?, userId?]) --> exists{File exists?<br/>deleted counts}
    exists -- no --> f404[404]
    exists -- yes --> rules{Upload rules pass?}
    rules -- no --> r400[400 with the reason<br/>old content untouched]
    rules -- yes --> race{Another replacement of this<br/>file saved first?}
    race -- yes --> c400[400 retry the upload<br/>old content untouched]
    race -- no --> ok[200 same id, new storageKey]
    ok --> was{Was it deleted?}
    was -- yes --> restored([Restored with the new content])
    was -- no --> live([Live with the new content])
```

Tenant and user stay as they were unless the request gives new ones. The previous version's bytes are deleted
straight away, or by the worker if that fails.

## Delete a file

```mermaid
flowchart TD
    start([DELETE /api/v1/files/id]) --> live{Live file?}
    live -- no, unknown or already deleted --> f404[404]
    live -- yes --> ok[204 No Content]
    ok --> after([Hidden from list, download and presign<br/>bytes kept, restorable any time])
```

## Restore a file

```mermaid
flowchart TD
    start([POST /api/v1/files/id/restore]) --> deleted{A deleted file?}
    deleted -- no, unknown or live --> f404[404]
    deleted -- yes --> bytes{Its bytes still in storage?}
    bytes -- no --> b404[404]
    bytes -- yes --> ok[204 No Content]
    ok --> after([Back as it was:<br/>same content, same metadata])
```

## Share a link

```mermaid
flowchart TD
    start([GET /api/v1/files/id/presign]) --> live{Live file?}
    live -- no --> f404[404]
    live -- yes --> can{Can the storage hand out<br/>direct links?}
    can -- "no: encryption on, or Local<br/>without PublicBaseUrl" --> r400[400 with the reason<br/>download through the API instead]
    can -- "yes: S3, Azure, Local with PublicBaseUrl" --> ok[200 url, expiresAtUtc]
    ok --> use([Anyone with the link fetches the bytes<br/>straight from storage until it expires])
```

A link points at the current version's bytes. A replacement deletes those bytes straight away, so links handed
out before it stop working; ask for a new one.

## A typical session

```mermaid
journey
    title Working with one document
    section Store
      Upload passport.pdf: 5: Caller
      Find it by tenant: 4: Caller
      Check its details: 4: Caller
    section Use
      Download it: 5: Caller
      Resume a download with Range: 4: Caller
      Share a time-limited link: 3: Caller
    section Change
      Replace with a new version: 4: Caller
      Delete it by mistake: 2: Caller
      Restore it: 5: Caller
```
