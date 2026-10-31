using FluentValidation;
using GM.API.Models;
using GM.FileStorage.Sample.Common.Resources;
using System;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>Get files list model.</summary>
public class GetFilesListModel : GetBaseListModel
{
    /// <summary>The id of the stored file.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Id")]
    public Guid? Id { get; set; }

    /// <summary>Only files uploaded for this tenant.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "TenantId")]
    public string? TenantId { get; set; }

    /// <summary>Only files uploaded for this user.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "UserId")]
    public string? UserId { get; set; }

    /// <summary>Only files whose storage key starts with this (e.g. <c>tenants/acme/</c>).</summary>
    [Display(ResourceType = typeof(StringResource), Name = "KeyPrefix")]
    public string? KeyPrefix { get; set; }

    /// <summary>Only files of this MIME type.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "ContentType")]
    public string? ContentType { get; set; }
}

/// <inheritdoc />
public class GetFilesListModelValidator : AbstractValidator<GetFilesListModel>
{
    /// <inheritdoc />
    public GetFilesListModelValidator()
    {
    }
}
