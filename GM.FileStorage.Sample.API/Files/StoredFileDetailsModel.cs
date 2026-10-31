using GM.API.Models;
using GM.FileStorage.Sample.Common.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>Stored file details model.</summary>
public class StoredFileDetailsModel : AuditableModel
{
    /// <summary>The catalog id.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Id")]
    public Guid Id { get; set; }
    /// <summary>The storage key.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Key")]
    public string? Key { get; set; }
    /// <summary>The original file name.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "FileName")]
    public string? FileName { get; set; }
    /// <summary>The MIME type.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "ContentType")]
    public string? ContentType { get; set; }
    /// <summary>Size in bytes.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Size")]
    public long Size { get; set; }
    /// <summary>Content checksum, when the provider computes one.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Checksum")]
    public string? Checksum { get; set; }
    /// <summary>Whether the file is publicly readable.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "IsPublic")]
    public bool IsPublic { get; set; }
    /// <summary>When the current content was stored (UTC).</summary>
    [Display(ResourceType = typeof(StringResource), Name = "UploadedAtUtc")]
    public DateTime UploadedAtUtc { get; set; }
    /// <summary>The metadata the file was uploaded with (tenantId, userId, …).</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}
