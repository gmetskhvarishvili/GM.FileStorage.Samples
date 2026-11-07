using System;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>One thing that happened to a file, and who did it.</summary>
public class FileHistoryEntryModel
{
    /// <summary>The entry's id; the X-Pagination header's LastKeyValue for the next page by key.</summary>
    public Guid Id { get; set; }
    /// <summary>What happened: <c>Uploaded</c>, <c>Downloaded</c>, <c>Deleted</c>, <c>Restored</c>, …</summary>
    public string Action { get; set; } = null!;
    /// <summary>When (UTC).</summary>
    public DateTime OccurredAtUtc { get; set; }
    /// <summary>The user (<c>X-User-Id</c>); null for a caller without one.</summary>
    public Guid? UserId { get; set; }
    /// <summary>The client application (<c>X-Client-Id</c>).</summary>
    public Guid? ClientId { get; set; }
    /// <summary>The session (<c>X-Session-Id</c>).</summary>
    public Guid? SessionId { get; set; }
    /// <summary>The caller's IP address.</summary>
    public string? IpAddress { get; set; }
    /// <summary>The caller's user agent (the client's, as the gateway forwards it).</summary>
    public string? UserAgent { get; set; }
    /// <summary>The request's correlation id, to find it in the logs.</summary>
    public string? CorrelationId { get; set; }
}
