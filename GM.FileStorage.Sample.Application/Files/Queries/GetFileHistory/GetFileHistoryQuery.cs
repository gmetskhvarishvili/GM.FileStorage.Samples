using FluentValidation;
using GM.EntityFramework.Domain.Common;
using GM.Exceptions;
using GM.FileStorage.Sample.Application.Common;
using GM.FileStorage.Sample.Common.Resources;
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
/// What happened to one of the caller's files, newest first: uploaded, downloaded, deleted, restored, each with who did
/// it. Read from the domain-event log, which records every change (and every download) with the caller's identity.
/// A deleted file's history stays readable.
/// </summary>
public class GetFileHistoryQuery : IRequest<List<FileHistoryEntryDto>>
{
    public const int MaxLimit = 100;

    public Guid Id { get; set; }

    /// <summary>At most this many entries, the newest (1 to <see cref="MaxLimit"/>).</summary>
    public int Limit { get; set; } = 50;
}

public class GetFileHistoryQueryValidator : AbstractValidator<GetFileHistoryQuery>
{
    public GetFileHistoryQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Limit).InclusiveBetween(1, GetFileHistoryQuery.MaxLimit);
    }
}

public class GetFileHistoryQueryHandler(IUnitOfWork unitOfWork, ICurrentActor actor)
    : IRequestHandler<GetFileHistoryQuery, List<FileHistoryEntryDto>>
{
    public async Task<List<FileHistoryEntryDto>> Handle(GetFileHistoryQuery request, CancellationToken cancellationToken)
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

        var events = await unitOfWork.StoredFileRepository.GetHistoryAsync(request.Id, request.Limit, cancellationToken);
        return events.Select(e => new FileHistoryEntryDto
        {
            Action = ActionOf(e.EventType),
            OccurredAtUtc = DateTime.SpecifyKind(e.OccurredOn, DateTimeKind.Utc),
            UserId = e.UserId,
            ClientId = e.ClientId,
            SessionId = e.SessionId,
            IpAddress = e.IpAddress,
            UserAgent = e.UserAgent,
            CorrelationId = e.CorrelationId,
        }).ToList();
    }

    // GMStoredFileUploadedDomainEvent -> Uploaded, StoredFileDownloadedDomainEvent -> Downloaded.
    private static string ActionOf(string eventType)
    {
        var action = eventType;
        foreach (var prefix in (string[])["GMStoredFile", "StoredFile"])
        {
            if (action.StartsWith(prefix, StringComparison.Ordinal))
            {
                action = action[prefix.Length..];
                break;
            }
        }

        return action.EndsWith("DomainEvent", StringComparison.Ordinal) ? action[..^"DomainEvent".Length] : action;
    }
}

public class FileHistoryEntryDto
{
    public required string Action { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public Guid? UserId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? SessionId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? CorrelationId { get; init; }
}
