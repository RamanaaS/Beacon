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

    /// <summary>
    /// Used by EscalationCheckerService to find escalation candidates. Filters in-memory after
    /// loading, not in SQL — CurrentStatus is a computed projection over the event log, not a
    /// stored column, so it can't be translated into a WHERE clause. Fine at this project's
    /// scale; a denormalized "current status" read-model column is the real fix once "load every
    /// incident to find the handful that are still open" stops being cheap.
    /// </summary>
    Task<IReadOnlyList<Incident>> GetOpenIncidentsAsync(CancellationToken ct = default);

    /// <summary>
    /// Backs the dashboard's incident list — every incident, open or resolved, newest first.
    /// Same in-memory-projection caveat as GetOpenIncidentsAsync: fine at this scale, a real
    /// read-model would be the fix once "load everything" stops being cheap.
    /// </summary>
    Task<IReadOnlyList<Incident>> GetAllAsync(CancellationToken ct = default);

    void Add(Incident incident);

    Task SaveChangesAsync(CancellationToken ct = default);
}
