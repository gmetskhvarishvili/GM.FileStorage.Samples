using GM.FileStorage.Sample.Common.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>The file a direct upload will write.</summary>
public class StartDirectUploadModel
{
    /// <summary>The original file name.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "FileName")]
    public string? FileName { get; set; }
    /// <summary>The MIME type; checked against the stored bytes when the upload completes.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "ContentType")]
    public string? ContentType { get; set; }
    /// <summary>The exact size in bytes; the stored object must match it.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Size")]
    public long Size { get; set; }
}

/// <summary>Where and how to send a direct upload's bytes.</summary>
public class DirectUploadModel
{
    /// <summary>Complete the upload with this id once the bytes are sent.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Id")]
    public Guid UploadId { get; set; }
    /// <summary>The presigned link to send the bytes to.</summary>
    public Uri? Url { get; set; }
    /// <summary>The HTTP method to send them with (PUT).</summary>
    public string? Method { get; set; }
    /// <summary>Headers the request must carry.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }
    /// <summary>When the link expires (UTC).</summary>
    public DateTime ExpiresAtUtc { get; set; }
}
