using GM.API.Models;
using GM.FileStorage.Sample.Common.Resources;
using System;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>Stored file details model.</summary>
public class StoredFileDetailsModel : AuditableModel
{
    /// <summary>The file's identifier (a GUID): use it for every other call.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Id")]
    public Guid Id { get; set; }
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
    /// <summary>When the current content was stored (UTC).</summary>
    [Display(ResourceType = typeof(StringResource), Name = "UploadedAtUtc")]
    public DateTime UploadedAtUtc { get; set; }
    /// <summary>The tenant the file belongs to (the uploader's <c>X-Tenant-Id</c>, or <c>public</c>).</summary>
    [Display(ResourceType = typeof(StringResource), Name = "TenantId")]
    public string? TenantId { get; set; }
    /// <summary>Who uploaded the file: their user id (<c>X-User-Id</c>), kept in the audit column; null when none was sent.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "UserId")]
    public Guid? CreatedBy { get; set; }
    /// <summary>When the stored bytes were last checked against this record (UTC); null until the first check.</summary>
    public DateTime? VerifiedAtUtc { get; set; }
    /// <summary>What that check found wrong (missing, wrong size, checksum mismatch); null when the bytes matched.</summary>
    public string? IntegrityProblem { get; set; }
}
