namespace GM.FileStorage.Sample.Domain.SeedWork;

/// <summary>
/// Upload metadata names: what the upload hands the key strategy (<c>{meta:tenantId}</c>) and stores as the object's
/// own metadata. The catalog keeps the tenant as a column, not as metadata.
/// </summary>
public static class StoredFileMetadataNames
{
    /// <summary>The tenant the file belongs to.</summary>
    public const string TenantId = "tenantId";

}
