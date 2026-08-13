using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

public sealed record TimelineEntryResponse(DateTimeOffset OccurredAt, string EventType, string Actor, string Description);

public sealed record IncidentResponse(
    IncidentId Id,
    string Title,
    Severity Severity,
    IncidentStatus Status,
    IReadOnlyList<string> Responders,
    bool IsEscalated,
    int EventCount,
    IReadOnlyList<TimelineEntryResponse> Timeline);

public sealed record GetIncidentQuery(IncidentId Id) : IRequest<IncidentResponse>;

public sealed class GetIncidentQueryHandler(IIncidentRepository repository)
    : IRequestHandler<GetIncidentQuery, IncidentResponse>
{
    public async Task<IncidentResponse> Handle(GetIncidentQuery request, CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new IncidentNotFoundException(request.Id);

        // Reuses the exact same event-to-text rendering the Excel/PDF reports (Phase 6) and the
        // postmortem prompt (Phase 8) use — the dashboard's timeline reads identically to the
        // report's timeline because it's the same formatter, not a fourth independent rendering.
        var timeline = incident.Events
            .OrderBy(e => e.OccurredAt)
            .Select(e => new TimelineEntryResponse(
                e.OccurredAt,
                IncidentEventFormatter.EventTypeLabel(e),
                IncidentEventFormatter.Actor(e),
                IncidentEventFormatter.Describe(e)))
            .ToList();

        return new IncidentResponse(
            incident.Id,
            incident.Title,
            incident.CurrentSeverity,
            incident.CurrentStatus,
            incident.Responders,
            incident.IsEscalated,
            incident.Events.Count,
            timeline);
    }
}
