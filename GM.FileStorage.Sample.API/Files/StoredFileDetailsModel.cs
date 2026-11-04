using GM.API.Models;
using GM.FileStorage.Sample.Common.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>Stored file details model.</summary>
public class StoredFileDetailsModel : AuditableModel
{
    /// <summary>The file's identifier (a GUID): use it for every other call.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Id")]
    public Guid Id { get; set; }
    /// <summary>The stored object's name, e.g. <c>9f1c….pdf</c>.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "StorageKey")]
    public string? StorageKey { get; set; }
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
    /// <summary>When the stored bytes were last checked against this record (UTC); null until the first check.</summary>
    public DateTime? VerifiedAtUtc { get; set; }
    /// <summary>What that check found wrong (missing, wrong size, checksum mismatch); null when the bytes matched.</summary>
    public string? IntegrityProblem { get; set; }
}
