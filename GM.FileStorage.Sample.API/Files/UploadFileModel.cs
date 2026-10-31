using FluentValidation;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileMetadataAggregate;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>Upload file model (query string; the file itself is the multipart <c>file</c> part).</summary>
public class UploadFileModel
{
    /// <summary>Tenant the file belongs to (default <c>public</c>).</summary>
    [Display(ResourceType = typeof(StringResource), Name = "TenantId")]
    public string? TenantId { get; set; }
    /// <summary>User the file belongs to (default <c>anon</c>).</summary>
    [Display(ResourceType = typeof(StringResource), Name = "UserId")]
    public string? UserId { get; set; }
    /// <summary>An explicit storage key; omit to have one generated from the tenant and user.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Key")]
    public string? Key { get; set; }
}

public class UploadFileModelValidator : AbstractValidator<UploadFileModel>
{
    public UploadFileModelValidator()
    {
        RuleFor(x => x.TenantId).MaximumLength(StoredFileMetadata.ValueMaxLength);
        RuleFor(x => x.UserId).MaximumLength(StoredFileMetadata.ValueMaxLength);
        RuleFor(x => x.Key).MaximumLength(StoredFile.KeyMaxLength);
    }
}
