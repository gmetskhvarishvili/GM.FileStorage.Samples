namespace GM.FileStorage.Sample.Persistence.Configuration;

/// <summary>
/// The sample's names for GM.FileStorage's tables: the same snake_case schema and tables GM.FileStorage uses by
/// default (<c>GMFileStorageTableNames</c>), without the <c>gm_</c> prefix. Each sample configuration overrides its
/// base's <c>Schema</c> and <c>Table</c> with these — showing how an application maps GM.FileStorage into its own
/// naming.
/// </summary>
public static class FileStorageTableNames
{
    public static class Schemas
    {
        public const string FileStorage = "file_storage";
    }

    public const string StoredFiles = "stored_files";
    public const string StoredFileMetadata = "stored_file_metadata";
}
