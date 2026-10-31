using GM.FileStorage.Sample.Common.Resources;
using System;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>A file that was just stored.</summary>
public class UploadedFileModel
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
}
