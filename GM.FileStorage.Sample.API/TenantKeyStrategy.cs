using GM.FileStorage;

namespace GM.FileStorage.Sample.API;

/// <summary>
/// KYC-style key strategy: scopes every file under its tenant and user, e.g.
/// <c>tenants/acme/users/u1/9f1c…a3.pdf</c>. Falls back to "public"/"anon" when those aren't given,
/// so the demo always works.
/// </summary>
public sealed class TenantKeyStrategy : IFileKeyStrategy
{
    public string GenerateKey(FileKeyContext context)
    {
        var tenant = context.Metadata.GetValueOrDefault("tenantId", "public");
        var user = context.Metadata.GetValueOrDefault("userId", "anon");
        var extension = Path.GetExtension(context.FileName);
        return $"tenants/{tenant}/users/{user}/{Guid.NewGuid():N}{extension}";
    }
}
