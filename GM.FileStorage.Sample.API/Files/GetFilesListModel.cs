using FluentValidation;
using GM.API.Models;
using GM.FileStorage.Sample.Common.Resources;
using System;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>Get files list model: the caller's tenant's files (<c>X-Tenant-Id</c>, else the shared <c>public</c> space).</summary>
public class GetFilesListModel : GetBaseListModel
{
    /// <summary>The id of the stored file.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "Id")]
    public Guid? Id { get; set; }

    /// <summary>Only files this user uploaded (their <c>X-User-Id</c>).</summary>
    [Display(ResourceType = typeof(StringResource), Name = "UserId")]
    public Guid? UserId { get; set; }

    /// <summary>Only files of this MIME type.</summary>
    [Display(ResourceType = typeof(StringResource), Name = "ContentType")]
    public string? ContentType { get; set; }
    /// <summary>Only files whose last integrity check found a problem (true), or none (false).</summary>
    public bool? IntegrityProblem { get; set; }
}

/// <inheritdoc />
public class GetFilesListModelValidator : AbstractValidator<GetFilesListModel>
{
    /// <inheritdoc />
    public GetFilesListModelValidator()
    {
    }
}
