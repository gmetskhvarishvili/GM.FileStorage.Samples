using System;
using System.Linq;

namespace GM.FileStorage.Sample.Application.Common;

/// <summary>
/// The upload rules GM.FileStorage applies inside <c>UploadAsync</c>, for the bytes that don't go through it:
/// direct uploads, written straight into storage.
/// </summary>
public static class UploadRules
{
    /// <summary>Whether <c>FileStorage:AllowedContentTypes</c> lets <paramref name="contentType"/> in (exact or <c>type/*</c>).</summary>
    public static bool IsAllowed(FileStorageOptions options, string contentType)
    {
        if (options.AllowedContentTypes.Count == 0)
            return true;

        var type = FileTypeDetector.Normalize(contentType);
        return options.AllowedContentTypes.Any(pattern =>
        {
            var allowed = FileTypeDetector.Normalize(pattern);
            return allowed.EndsWith("/*", StringComparison.Ordinal)
                ? type.StartsWith(allowed[..^1], StringComparison.Ordinal)
                : type == allowed;
        });
    }
}
