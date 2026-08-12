using Beacon.Application.Common;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

public sealed record DeclareIncidentRequest(
    string Title,
    Severity Severity,
    string DeclaredByUserId,
    string AlertFingerprint);

/// <summary>
/// Plain orchestration class, deliberately not a MediatR command. At this complexity — one
/// aggregate, one repository call — MediatR would be pure ceremony. It shows up in Phase 4
/// once the incident engine actually needs command/query separation (escalation evaluation,
/// domain-event side effects); see ARCHITECTURE.md Section 9 for why CQRS is scoped there and
/// not applied blanket across the whole app.
/// </summary>
public sealed class DeclareIncidentHandler(IIncidentRepository repository, IClock clock)
{
    public async Task<IncidentId> HandleAsync(DeclareIncidentRequest request, CancellationToken ct = default)
    {
        var incident = Incident.Declare(
            request.Title,
            request.Severity,
            request.DeclaredByUserId,
            request.AlertFingerprint,
            clock.UtcNow);

        repository.Add(incident);
        await repository.SaveChangesAsync(ct);

        return incident.Id;
    }
}
