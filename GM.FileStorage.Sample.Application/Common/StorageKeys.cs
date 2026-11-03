namespace GM.FileStorage.Sample.Application.Common;

/// <summary>
/// Splits a storage key (<c>documents/a5/a5ad….pdf</c>) for display: the stored object's name
/// (<c>a5ad….pdf</c>) and the folder it sits in (<c>documents/a5/</c>).
/// </summary>
public static class StorageKeys
{
    /// <summary>The object's name, without its folders.</summary>
    public static string Name(string storageKey)
    {
        var slash = storageKey.LastIndexOf('/');
        return slash < 0 ? storageKey : storageKey[(slash + 1)..];
    }

    /// <summary>The folder the object sits in, ending with <c>/</c>; empty at the root.</summary>
    public static string Folder(string storageKey)
    {
        var slash = storageKey.LastIndexOf('/');
        return slash < 0 ? string.Empty : storageKey[..(slash + 1)];
    }
}
