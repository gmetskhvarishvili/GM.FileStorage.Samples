using System;

namespace GM.FileStorage.Sample.Application.Common;

/// <summary>
/// Direct uploads (<c>DirectUploads</c> in appsettings.json): large files the client writes straight into storage
/// through a presigned link, instead of through the API.
/// </summary>
public class DirectUploadOptions
{
    /// <summary>Largest file accepted. Default 5 GB, the most S3 takes in a single PUT.</summary>
    public long MaxFileSize { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>How long the write link stays valid, and so how long the client has to upload and complete.</summary>
    public TimeSpan LinkExpiry { get; set; } = TimeSpan.FromHours(1);
}
