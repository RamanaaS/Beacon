using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

public sealed record IncidentResponse(
    IncidentId Id,
    string Title,
    Severity Severity,
    IncidentStatus Status,
    IReadOnlyList<string> Responders,
    bool IsEscalated,
    int EventCount);

public sealed record GetIncidentQuery(IncidentId Id) : IRequest<IncidentResponse>;

public sealed class GetIncidentQueryHandler(IIncidentRepository repository)
    : IRequestHandler<GetIncidentQuery, IncidentResponse>
{
    public async Task<IncidentResponse> Handle(GetIncidentQuery request, CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new IncidentNotFoundException(request.Id);

        return new IncidentResponse(
            incident.Id,
            incident.Title,
            incident.CurrentSeverity,
            incident.CurrentStatus,
            incident.Responders,
            incident.IsEscalated,
            incident.Events.Count);
    }
}
