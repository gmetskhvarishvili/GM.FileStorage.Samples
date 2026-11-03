using GM.FileStorage.Sample.Common.Resources;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>What happened to one file of a batch upload.</summary>
public class UploadFileResultModel
{
    /// <summary>The file's name as sent.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "FileName")]
    public string? FileName { get; set; }
    /// <summary>Whether the file was stored.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Succeeded")]
    public bool Succeeded { get; set; }
    /// <summary>The stored file, when it succeeded.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "File")]
    public UploadedFileModel? File { get; set; }
    /// <summary>Why the file wasn't stored, when it failed.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Error")]
    public string? Error { get; set; }
}
