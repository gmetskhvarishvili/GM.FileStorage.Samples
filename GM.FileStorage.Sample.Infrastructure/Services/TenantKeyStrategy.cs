using GM.FileStorage.Sample.Domain.SeedWork;
using System;
using System.Collections.Generic;
using System.IO;

namespace GM.FileStorage.Sample.Infrastructure.Services;

/// <summary>
/// KYC-style key strategy: scopes every file under its tenant and user, e.g.
/// <c>tenants/acme/users/u1/9f1c…a3.pdf</c>. Falls back to "public"/"anon" when those aren't given,
/// so the demo always works.
/// </summary>
public sealed class TenantKeyStrategy : IFileKeyStrategy
{
    /// <inheritdoc />
    public string GenerateKey(FileKeyContext context)
    {
        var tenant = context.Metadata.GetValueOrDefault(StoredFileMetadataNames.TenantId, "public");
        var user = context.Metadata.GetValueOrDefault(StoredFileMetadataNames.UserId, "anon");
        var extension = Path.GetExtension(context.FileName);
        return $"tenants/{tenant}/users/{user}/{Guid.NewGuid():N}{extension}";
    }
}
