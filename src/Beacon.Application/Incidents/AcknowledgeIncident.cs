using Beacon.Application.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

public sealed record AcknowledgeIncidentCommand(IncidentId IncidentId, string UserId) : IRequest;

public sealed class AcknowledgeIncidentCommandHandler(IIncidentRepository repository, IClock clock)
    : IRequestHandler<AcknowledgeIncidentCommand>
{
    public async Task Handle(AcknowledgeIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, cancellationToken)
            ?? throw new IncidentNotFoundException(request.IncidentId);

        incident.Acknowledge(request.UserId, clock.UtcNow);

        await repository.SaveChangesAsync(cancellationToken);
    }
}
