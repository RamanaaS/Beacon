using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

/// <summary>
/// Owned by Application, implemented by Infrastructure — Application never references EF Core.
/// This is the one Repository interface in the project, and it earns its place: it's the seam
/// that lets DeclareIncidentHandler etc. be unit-tested with a fake, and it hides how an
/// event-sourced aggregate gets reconstructed (Incident.Rehydrate) from everything above it.
/// </summary>
public interface IIncidentRepository
{
    Task<Incident?> GetByIdAsync(IncidentId id, CancellationToken ct = default);

    void Add(Incident incident);

    Task SaveChangesAsync(CancellationToken ct = default);
}
