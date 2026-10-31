namespace GM.FileStorage.Sample.Domain.SeedWork;

/// <summary>
/// Well-known metadata names the sample stores on every upload. Shared so the upload handler (which writes them),
/// the key strategy (which shapes the storage key from them), the list specification (which filters by them) and
/// the tests all agree on the exact strings.
/// </summary>
public static class StoredFileMetadataNames
{
    /// <summary>The tenant the file belongs to.</summary>
    public const string TenantId = "tenantId";

    /// <summary>The user the file belongs to.</summary>
    public const string UserId = "userId";
}
