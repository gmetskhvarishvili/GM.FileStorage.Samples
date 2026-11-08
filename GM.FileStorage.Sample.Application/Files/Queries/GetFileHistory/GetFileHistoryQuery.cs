using FluentValidation;
using GM.API.Application.Models;
using GM.EntityFramework.Domain.Common;
using GM.EntityFramework.Domain.Specifications;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.BoundedContext.AuditBoundedContext.AuditAggregate.Specifications;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Queries.GetFileHistory;

/// <summary>
/// What happened to one of the caller's files: uploaded, downloaded, deleted, restored, each with who did it. Read
/// from the domain-event log, which records every change (and every download) with the caller's identity. Filtered,
/// ordered (newest first by default) and paged like any list; a deleted file's history stays readable.
/// </summary>
public class GetFileHistoryQuery : GetBaseListQuery, IRequest<PagedListDto<FileHistoryEntryDto>>
{
    public Guid Id { get; set; }

    /// <summary>Only this action: <c>Uploaded</c>, <c>Downloaded</c>, <c>Deleted</c>, <c>Restored</c>, …</summary>
    public string? Action { get; set; }

    public Guid? UserId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? SessionId { get; set; }
    public string? IpAddress { get; set; }

    /// <summary>
    /// The fields <see cref="GetBaseListQuery.OrderBy"/> may name, and what they sort the log by. An action sorts by its
    /// event type, all GM.FileStorage's GMStoredFile{Action}DomainEvent, so in the order of the action's name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> OrderFields =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Id",
            ["action"] = "EventType",
            ["occurredAtUtc"] = "OccurredOn",
            ["userId"] = "UserId",
            ["clientId"] = "ClientId",
            ["sessionId"] = "SessionId",
            ["ipAddress"] = "IpAddress",
            ["userAgent"] = "UserAgent",
        };

    /// <summary>
    /// <see cref="GetBaseListQuery.OrderBy"/> in the log's columns: <c>"occurredAtUtc desc, userId"</c> becomes
    /// <c>"OccurredOn desc, UserId asc"</c>. Newest first when none is given.
    /// </summary>
    public string ToLogOrder()
    {
        if (string.IsNullOrWhiteSpace(OrderBy))
            return "OccurredOn desc";

        return string.Join(", ", OrderBy.Split(',').Select(term =>
        {
            var parts = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var direction = parts.Length > 1 && parts[1].StartsWith("desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
            return $"{OrderFields[parts[0]]} {direction}";
        }));
    }

    /// <summary>
    /// Whether <paramref name="orderBy"/> is valid: each <c>field [asc|desc]</c> term names a known field, and paging by
    /// key (<paramref name="byKey"/>) orders by one column of the log (not by action).
    /// </summary>
    public static bool IsValidOrder(string? orderBy, bool byKey) =>
        IsKnownOrder(orderBy) && (!byKey || string.IsNullOrWhiteSpace(orderBy)
                                  || (!orderBy.Contains(',') && !orderBy.TrimStart().StartsWith("action", StringComparison.OrdinalIgnoreCase)));

    private static bool IsKnownOrder(string? orderBy) =>
        string.IsNullOrWhiteSpace(orderBy) || orderBy.Split(',').All(term =>
        {
            var parts = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length is 1 or 2
                   && OrderFields.ContainsKey(parts[0])
                   && (parts.Length == 1 || parts[1].ToLowerInvariant() is "asc" or "desc" or "ascending" or "descending");
        });
}

public class GetFileHistoryQueryHandler(IUnitOfWork unitOfWork, ICurrentActor actor)
    : IRequestHandler<GetFileHistoryQuery, PagedListDto<FileHistoryEntryDto>>
{
    public async Task<PagedListDto<FileHistoryEntryDto>> Handle(GetFileHistoryQuery request, CancellationToken cancellationToken)
    {
        // Another tenant's file has no history for you: 404, as for an unknown id.
        var owned = await unitOfWork.StoredFileRepository
            .Query(true, null)
            .OwnedBy(actor.TenantOrShared())
            .AnyAsync(x => x.Id == request.Id, cancellationToken);
        if (!owned)
        {
            throw new NotFoundException(
                StringResource.StoredFile,
                StringResource.Id,
                request.Id);
        }

        var dateRange = request.ToAuditDateRange();
        var paging = request.ToPagingOptions();
        var order = request.ToLogOrder();
        // Paging by number: entries of the same instant (a delete also hides and deactivates) keep their place
        // across pages. Paging by key breaks such ties by the key itself.
        if (!paging.IsKeysetPaging)
            order += ", Id desc";

        var eventType = EventTypeOf(request.Action);

        // The file's own entries in the log.
        DomainEventLogSpecification Specification(PagingOptions pagingOptions, OrderingOptions ordering) =>
            new(nameof(StoredFile), request.Id.ToString(), eventType, request.UserId, request.ClientId,
                request.SessionId, request.IpAddress, dateRange, pagingOptions, ordering);

        long? totalCount = paging.IncludeTotalCount
            ? await unitOfWork.DomainEventLogRepository.CountAsync(
                Specification(PagingOptions.None, OrderingOptions.None), cancellationToken)
            : null;

        var events = await unitOfWork.DomainEventLogRepository.ListAsync(
            Specification(paging, new OrderingOptions { OrderBy = order }), cancellationToken);

        var items = events.Select(e => new FileHistoryEntryDto
        {
            Id = e.Id,
            Action = ActionOf(e.EventType),
            OccurredAtUtc = DateTime.SpecifyKind(e.OccurredOn, DateTimeKind.Utc),
            UserId = e.UserId,
            ClientId = e.ClientId,
            SessionId = e.SessionId,
            IpAddress = e.IpAddress,
            UserAgent = e.UserAgent,
            CorrelationId = e.CorrelationId,
        }).ToList();

        return request.ToPagedListDto(events, items, totalCount);
    }

    // An action's event type: GM.FileStorage's GMStoredFile{Action}DomainEvent.
    private static string? EventTypeOf(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return null;

        var name = char.ToUpperInvariant(action.Trim()[0]) + action.Trim()[1..];
        return $"GMStoredFile{name}DomainEvent";
    }

    // GMStoredFileUploadedDomainEvent -> Uploaded.
    private static string ActionOf(string eventType)
    {
        var action = eventType.StartsWith("GMStoredFile", StringComparison.Ordinal)
            ? eventType["GMStoredFile".Length..]
            : eventType;
        return action.EndsWith("DomainEvent", StringComparison.Ordinal) ? action[..^"DomainEvent".Length] : action;
    }
}

public class FileHistoryEntryDto
{
    public Guid Id { get; init; }
    public required string Action { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public Guid? UserId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? SessionId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? CorrelationId { get; init; }
}
