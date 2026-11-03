using System;
using System.IO;

namespace GM.FileStorage.Sample.Infrastructure.Services;

/// <summary>
/// Keys are just a random id plus the file's extension, e.g. <c>9f1cb2…a3.pdf</c> — no folders, no tenant, user or
/// date. Who a file belongs to lives in its searchable metadata rows instead (replaces GM.FileStorage's default
/// date-partitioned strategy).
/// </summary>
public sealed class FlatFileKeyStrategy : IFileKeyStrategy
{
    /// <inheritdoc />
    public string GenerateKey(FileKeyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return $"{Guid.NewGuid():N}{Path.GetExtension(context.FileName)}";
    }
}
