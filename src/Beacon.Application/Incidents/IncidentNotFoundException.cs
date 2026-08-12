using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

public sealed class IncidentNotFoundException(IncidentId id)
    : Exception($"Incident {id} was not found.")
{
    public IncidentId IncidentId { get; } = id;
}
