using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

public sealed record IncidentSummaryResponse(
    IncidentId Id,
    string Title,
    Severity Severity,
    IncidentStatus Status,
    bool IsEscalated,
    DateTimeOffset DeclaredAt);

public sealed record ListIncidentsQuery : IRequest<IReadOnlyList<IncidentSummaryResponse>>;

public sealed class ListIncidentsQueryHandler(IIncidentRepository repository)
    : IRequestHandler<ListIncidentsQuery, IReadOnlyList<IncidentSummaryResponse>>
{
    public async Task<IReadOnlyList<IncidentSummaryResponse>> Handle(
        ListIncidentsQuery request, CancellationToken cancellationToken)
    {
        var incidents = await repository.GetAllAsync(cancellationToken);

        return incidents
            .OrderByDescending(i => i.DeclaredAt)
            .Select(i => new IncidentSummaryResponse(i.Id, i.Title, i.CurrentSeverity, i.CurrentStatus, i.IsEscalated, i.DeclaredAt))
            .ToList();
    }
}
