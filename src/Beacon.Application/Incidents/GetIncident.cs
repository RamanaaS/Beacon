using Beacon.Domain.Common;
using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

public sealed record IncidentResponse(
    IncidentId Id,
    string Title,
    Severity Severity,
    IncidentStatus Status,
    IReadOnlyList<string> Responders,
    int EventCount);

public sealed class GetIncidentHandler(IIncidentRepository repository)
{
    public async Task<IncidentResponse> HandleAsync(IncidentId id, CancellationToken ct = default)
    {
        var incident = await repository.GetByIdAsync(id, ct)
            ?? throw new IncidentNotFoundException(id);

        return new IncidentResponse(
            incident.Id,
            incident.Title,
            incident.CurrentSeverity,
            incident.CurrentStatus,
            incident.Responders,
            incident.Events.Count);
    }
}
