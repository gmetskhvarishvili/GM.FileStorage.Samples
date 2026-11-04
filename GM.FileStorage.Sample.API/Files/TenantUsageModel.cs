using GM.FileStorage.Sample.Common.Resources;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>A tenant's storage use against its quota.</summary>
public class TenantUsageModel
{
    [Display(ResourceType = typeof(StringResource), Name = "TenantId")]
    public string? TenantId { get; set; }
    /// <summary>Bytes stored: files, deleted ones until purged.</summary>
    public long UsedBytes { get; set; }
    /// <summary>The tenant's quota in bytes; null when unlimited.</summary>
    public long? QuotaBytes { get; set; }
    /// <summary>Files counted, deleted ones included.</summary>
    public int FileCount { get; set; }
}
