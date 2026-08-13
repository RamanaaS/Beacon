using Beacon.Application.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

/// <summary>
/// Issued two ways: directly, by a human hitting POST /api/incidents/{id}/escalate, and
/// automatically, by EscalationCheckerService acting on EscalationPolicy's verdict. Same command,
/// same handler, same validation either way — that's the actual payoff of routing both through
/// MediatR instead of the background worker calling repository/domain code directly.
/// </summary>
public sealed record EscalateIncidentCommand(IncidentId IncidentId, string EscalatedByUserId, string EscalatedTo) : IRequest;

public sealed class EscalateIncidentCommandHandler(IIncidentRepository repository, IClock clock)
    : IRequestHandler<EscalateIncidentCommand>
{
    public async Task Handle(EscalateIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, cancellationToken)
            ?? throw new IncidentNotFoundException(request.IncidentId);

        incident.Escalate(request.EscalatedByUserId, request.EscalatedTo, clock.UtcNow);

        await repository.SaveChangesAsync(cancellationToken);
    }
}
