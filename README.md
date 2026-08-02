<p align="center">
  <img src="icon.png" alt="GM.FileStorage Samples" width="140" height="140" />
</p>

# GM.FileStorage Samples

[![CI](https://github.com/gmetskhvarishvili/GM.FileStorage.Samples/actions/workflows/ci.yml/badge.svg)](https://github.com/gmetskhvarishvili/GM.FileStorage.Samples/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A minimal ASP.NET Core Web API showing **[GM.FileStorage](https://www.nuget.org/packages/GM.FileStorage)**
with the **[GM.FileStorage.Local](https://www.nuget.org/packages/GM.FileStorage.Local)** provider:
streaming upload/download, a **tenant/user-scoped key strategy** (the KYC pattern), delete, and
presigned links — all behind the provider-agnostic `IFileStorageService`. Targets **.NET 10**.

## What it demonstrates

- `AddGMFileStorage(config)` + `AddGMLocalFileStorage(config)` — the backend is chosen by
  `FileStorage:Provider` (here `"Local"`); reference the S3/Azure packages later to switch by config.
- A custom `IFileKeyStrategy` (`TenantKeyStrategy`) that scopes files as
  `tenants/{tenantId}/users/{userId}/{guid}{ext}`.
- **Streaming** upload/download — `Results.Stream` sends the file straight from storage.

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/files?tenantId=&userId=` | Upload a multipart `file`; returns the scoped key + size + checksum |
| `GET` | `/files/{**key}` | Download the file (streamed) |
| `DELETE` | `/files/{**key}` | Delete the file |
| `GET` | `/presign/{**key}` | A time-limited link (needs `FileStorage:Local:PublicBaseUrl`) |

```bash
dotnet run --project GM.FileStorage.Sample.API

curl -F "file=@passport.pdf" "http://localhost:5xxx/files?tenantId=acme&userId=u1"
# { "key": "tenants/acme/users/u1/9f1c…a3.pdf", "size": 12345, "checksum": "…", "contentType": "application/pdf" }

curl -O "http://localhost:5xxx/files/tenants/acme/users/u1/9f1c…a3.pdf"
```

Files land under `FileStorage:Local:RootPath` (default `App_Data/file-storage`). Swap to S3/Azure by
referencing those provider packages and changing `FileStorage:Provider` — no code change.

## Testing

```bash
dotnet test
```

The tests boot the API in-memory with `WebApplicationFactory` against a throwaway storage root and
assert the upload → download round-trip — no external storage required.

## License

MIT — see [LICENSE](LICENSE).
