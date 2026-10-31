using System;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>A time-limited link to a file.</summary>
public class PresignedUrlModel
{
    /// <summary>The link.</summary>
    public Uri? Url { get; set; }
    /// <summary>When the link stops working (UTC).</summary>
    public DateTime ExpiresAtUtc { get; set; }
}
