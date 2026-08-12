using Beacon.Application.Common;
using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

public sealed record AcknowledgeIncidentRequest(IncidentId IncidentId, string UserId);

public sealed class AcknowledgeIncidentHandler(IIncidentRepository repository, IClock clock)
{
    public async Task HandleAsync(AcknowledgeIncidentRequest request, CancellationToken ct = default)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, ct)
            ?? throw new IncidentNotFoundException(request.IncidentId);

        incident.Acknowledge(request.UserId, clock.UtcNow);

        await repository.SaveChangesAsync(ct);
    }
}
