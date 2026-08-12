using Beacon.Application.Common;
using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

public sealed record ResolveIncidentRequest(IncidentId IncidentId, string UserId, string Summary);

public sealed class ResolveIncidentHandler(IIncidentRepository repository, IClock clock)
{
    public async Task HandleAsync(ResolveIncidentRequest request, CancellationToken ct = default)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, ct)
            ?? throw new IncidentNotFoundException(request.IncidentId);

        incident.Resolve(request.UserId, request.Summary, clock.UtcNow);

        await repository.SaveChangesAsync(ct);
    }
}
