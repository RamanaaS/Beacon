using Beacon.Application.Common;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

/// <summary>
/// Now a real MediatR command — this phase is where that stops being ceremony. See
/// ARCHITECTURE.md Section 9 and LEARNING_GUIDE.md Phase 5 for why: escalation checking (a
/// background worker, not an HTTP request) needs to issue the same "act on an incident" commands
/// as the API does, and CQRS is what gives both callers one shared entry point without the
/// worker depending on Beacon.Api or the API depending on worker internals.
/// </summary>
public sealed record DeclareIncidentCommand(
    string Title,
    Severity Severity,
    string DeclaredByUserId,
    string AlertFingerprint) : IRequest<IncidentId>;

public sealed class DeclareIncidentCommandHandler(IIncidentRepository repository, IClock clock)
    : IRequestHandler<DeclareIncidentCommand, IncidentId>
{
    public async Task<IncidentId> Handle(DeclareIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = Incident.Declare(
            request.Title,
            request.Severity,
            request.DeclaredByUserId,
            request.AlertFingerprint,
            clock.UtcNow);

        repository.Add(incident);
        await repository.SaveChangesAsync(cancellationToken);

        return incident.Id;
    }
}
