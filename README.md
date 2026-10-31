<p align="center">
  <img src="icon.png" alt="GM.FileStorage Samples" width="140" height="140" />
</p>

# GM.FileStorage Samples

[![CI](https://github.com/gmetskhvarishvili/GM.FileStorage.Samples/actions/workflows/ci.yml/badge.svg)](https://github.com/gmetskhvarishvili/GM.FileStorage.Samples/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A minimal ASP.NET Core Web API showing **[GM.FileStorage](https://www.nuget.org/packages/GM.FileStorage)**
with the **[GM.FileStorage.Local](https://www.nuget.org/packages/GM.FileStorage.Local)** provider:
streaming upload/download, a **tenant/user-scoped key strategy** (the KYC pattern), delete, and
presigned links — all behind the provider-agnostic `IFileStorageService`. It's hosted on
**[GM.API](https://www.nuget.org/packages/GM.API)**, so it gets Swagger, versioning, RFC 7807 errors,
request logging, health checks and secrets out of the box. Targets **.NET 10**.

## What it demonstrates

- **All three backends wired at once** — `AddGMLocalFileStorage` + `AddGMS3FileStorage` +
  `AddGMAzureBlobFileStorage`. `FileStorage:Provider` (default `"Local"`) chooses which one is
  active, so switching disk → S3 → Azure is a **config change, not a code change**. The cloud
  clients are only constructed when their provider is selected, so it runs on disk with no cloud
  config.
- A custom `IFileKeyStrategy` (`TenantKeyStrategy`) that scopes files as
  `tenants/{tenantId}/users/{userId}/{guid}{ext}`.
- **Streaming** upload/download — the upload is read straight from the request into storage, and
  `File(...)` streams the download back; neither is buffered whole.
- **GM.API hosting** — `CreateGMBuilder` / `ConfigureGMServices` / `UseGMServices` in `Program.cs`,
  and a versioned `FilesController : BaseController`. That brings:
  - **Swagger UI** at `/` (from `SwaggerDocOptions`, styled by the files in `Assets/`);
  - **health checks** at `/health/live`, `/health/ready` and `/health/startup`;
  - **request logging** (Serilog) that only logs text bodies up to 64 KB, so an upload is
    described (`[body not logged: multipart/form-data, … bytes]`) and never read, and a download
    streams to the client untouched;
  - **secrets** layered into configuration, so cloud keys can live in `appsecrets.json` or the
    store named by `SECRETS_PROVIDER` instead of `appsettings.json`.

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/v1/files?tenantId=&userId=` | Upload a multipart `file`; returns the scoped key + size + checksum |
| `GET` | `/api/v1/files/{**key}` | Download the file (streamed) |
| `DELETE` | `/api/v1/files/{**key}` | Delete the file |
| `GET` | `/api/v1/presign/{**key}` | A time-limited link (needs `FileStorage:Local:PublicBaseUrl`) |
| `GET` | `/health/live` / `/health/ready` / `/health/startup` | Liveness / readiness / startup probes (GM.API) |
| `GET` | `/` | Swagger UI |

```bash
dotnet run --project GM.FileStorage.Sample.API

curl -F "file=@passport.pdf" "http://localhost:5xxx/api/v1/files?tenantId=acme&userId=u1"
# { "key": "tenants/acme/users/u1/9f1c…a3.pdf", "size": 12345, "checksum": "…", "contentType": "application/pdf" }

curl -O "http://localhost:5xxx/api/v1/files/tenants/acme/users/u1/9f1c…a3.pdf"
```

Files land under `FileStorage:Local:RootPath` (default `App_Data/file-storage`). To run on S3/MinIO
or Azure Blob, fill in `FileStorage:S3` / `FileStorage:AzureBlob` in `appsettings.json` and set
`FileStorage:Provider` to `"S3"` or `"AzureBlob"` — no code change. Keep `FileStorage:S3:SecretKey`
and `FileStorage:AzureBlob:ConnectionString` out of `appsettings.json`: GM.API reads them from
`appsecrets.json` or from the secrets store named by `SECRETS_PROVIDER`.

## Testing

```bash
dotnet test
```

The tests boot the API in-memory with `WebApplicationFactory` against a throwaway storage root and
assert the upload → download round-trip — no external storage required.

## License

MIT — see [LICENSE](LICENSE).
