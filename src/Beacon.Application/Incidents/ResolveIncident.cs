using Beacon.Application.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

public sealed record ResolveIncidentCommand(IncidentId IncidentId, string UserId, string Summary) : IRequest;

public sealed class ResolveIncidentCommandHandler(IIncidentRepository repository, IClock clock)
    : IRequestHandler<ResolveIncidentCommand>
{
    public async Task Handle(ResolveIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, cancellationToken)
            ?? throw new IncidentNotFoundException(request.IncidentId);

        incident.Resolve(request.UserId, request.Summary, clock.UtcNow);

        await repository.SaveChangesAsync(cancellationToken);
    }
}
