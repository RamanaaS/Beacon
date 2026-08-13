using Beacon.Application.Incidents;
using Beacon.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Infrastructure.Incidents;

public sealed class IncidentRepository(BeaconDbContext context) : IIncidentRepository
{
    public async Task<Incident?> GetByIdAsync(IncidentId id, CancellationToken ct = default) =>
        await context.Incidents
            .Include("_events")
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<Incident>> GetOpenIncidentsAsync(CancellationToken ct = default)
    {
        var all = await context.Incidents.Include("_events").ToListAsync(ct);
        return all.Where(i => i.CurrentStatus != IncidentStatus.Resolved).ToList();
    }

    public async Task<IReadOnlyList<Incident>> GetAllAsync(CancellationToken ct = default) =>
        await context.Incidents.Include("_events").ToListAsync(ct);

    public void Add(Incident incident) => context.Incidents.Add(incident);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
