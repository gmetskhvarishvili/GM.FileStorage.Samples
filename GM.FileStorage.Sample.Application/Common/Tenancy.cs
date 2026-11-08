using GM.EntityFramework.Domain.Common;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;

namespace GM.FileStorage.Sample.Application.Common;

/// <summary>
/// Whose files a request may touch. The caller comes from <see cref="ICurrentActor"/>, as everywhere in GM: the
/// <c>X-Tenant-Id</c> and <c>X-User-Id</c> headers, or the token's <c>tenant_id</c> and user claims once sign-in is
/// added. Both are optional. A file belongs to the tenant that uploaded it, and to every other tenant it's simply not
/// there (404; GM.FileStorage.Catalog's <c>OwnedBy</c> filters by it). Callers without a tenant share one space,
/// <see cref="SharedTenant"/>, which no tenant can see into.
/// </summary>
public static class Tenancy
{
    /// <summary>
    /// The tenant recorded for callers that name none. A real tenant is a GUID, so no caller can claim this one.
    /// </summary>
    public const string SharedTenant = "public";

    /// <summary>The caller's tenant, as stored in <see cref="StoredFile.TenantId"/>.</summary>
    public static string TenantOrShared(this ICurrentActor actor) => actor.TenantId?.ToString() ?? SharedTenant;
}
