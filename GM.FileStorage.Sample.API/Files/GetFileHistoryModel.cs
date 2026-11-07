using FluentValidation;
using GM.API.Models;
using GM.FileStorage.Sample.Application.Files.Queries.GetFileHistory;
using GM.FileStorage.Sample.Common.Resources;
using System;
using System.ComponentModel.DataAnnotations;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// Get file history model: filters, ordering and paging for one file's history. <c>OrderBy</c> takes
/// <c>occurredAtUtc</c>, <c>action</c>, <c>userId</c>, <c>clientId</c>, <c>sessionId</c>, <c>ipAddress</c>,
/// <c>userAgent</c> or <c>id</c>, each optionally <c>asc</c> or <c>desc</c> (newest first by default);
/// <c>CreatedAtFrom</c> / <c>CreatedAtTo</c> bound when it happened.
/// </summary>
public class GetFileHistoryModel : GetBaseListModel
{
    /// <summary>Only this action: <c>Uploaded</c>, <c>Downloaded</c>, <c>Deleted</c>, <c>Restored</c>, …</summary>
    public string? Action { get; set; }

    /// <summary>Only what this user did (<c>X-User-Id</c>).</summary>
    [Display(ResourceType = typeof(StringResource), Name = "UserId")]
    public Guid? UserId { get; set; }

    /// <summary>Only what was done through this client application (<c>X-Client-Id</c>).</summary>
    public Guid? ClientId { get; set; }

    /// <summary>Only what was done in this session (<c>X-Session-Id</c>).</summary>
    public Guid? SessionId { get; set; }

    /// <summary>Only what was done from this IP address.</summary>
    public string? IpAddress { get; set; }
}

/// <inheritdoc />
public class GetFileHistoryModelValidator : AbstractValidator<GetFileHistoryModel>
{
    /// <inheritdoc />
    public GetFileHistoryModelValidator()
    {
        RuleFor(x => x.OrderBy)
            .Must((model, orderBy) => GetFileHistoryQuery.IsValidOrder(orderBy, !string.IsNullOrWhiteSpace(model.LastKeyValue)))
            .WithMessage($"OrderBy takes {string.Join(", ", GetFileHistoryQuery.OrderFields.Keys)}, each optionally asc or desc; " +
                         "with LastKeyValue, just one of them other than action.");
    }
}
