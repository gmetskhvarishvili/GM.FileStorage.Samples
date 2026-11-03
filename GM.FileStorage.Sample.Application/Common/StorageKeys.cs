namespace GM.FileStorage.Sample.Application.Common;

/// <summary>
/// What callers see of a storage key: just the stored object's name (<c>a5ad….pdf</c> of
/// <c>documents/a5/a5ad….pdf</c>). The folders are an internal layout detail.
/// </summary>
public static class StorageKeys
{
    /// <summary>The object's name, without its folders.</summary>
    public static string Name(string storageKey)
    {
        var slash = storageKey.LastIndexOf('/');
        return slash < 0 ? storageKey : storageKey[(slash + 1)..];
    }
}
