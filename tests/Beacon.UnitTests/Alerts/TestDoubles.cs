using Beacon.Application.Alerts;
using Beacon.Application.Common;
using Beacon.Application.Incidents;
using Beacon.Domain.Alerts;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.UnitTests.Alerts;

/// <summary>
/// Hand-written fakes rather than a mocking library — there's exactly one behavior to fake per
/// interface, and a real (if minimal) implementation exercises IngestAlertHandler's actual
/// orchestration logic more faithfully than a library stubbing out return values would. This is
/// also the answer to "why doesn't Beacon.Infrastructure need to be running for this test" —
/// these are the same interfaces Infrastructure implements for real (RedisRecentAlertsStore,
/// IncidentRepository), swapped for something that needs no database and no Redis connection.
/// </summary>
internal sealed class InMemoryRecentAlertsStore : IRecentAlertsStore
{
    private readonly List<Alert> _alerts = [];

    public Task<IReadOnlyList<Alert>> GetRecentAsync(string fingerprint, TimeSpan window, CancellationToken ct = default)
    {
        IReadOnlyList<Alert> matches = _alerts.Where(a => a.Fingerprint == fingerprint).ToList();
        return Task.FromResult(matches);
    }

    public Task RecordAsync(Alert alert, TimeSpan retention, CancellationToken ct = default)
    {
        _alerts.Add(alert);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryIncidentRepository : IIncidentRepository
{
    private readonly Dictionary<IncidentId, Incident> _incidents = [];

    public Task<Incident?> GetByIdAsync(IncidentId id, CancellationToken ct = default) =>
        Task.FromResult(_incidents.GetValueOrDefault(id));

    public Task<IReadOnlyList<Incident>> GetOpenIncidentsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Incident> open = _incidents.Values.Where(i => i.CurrentStatus != IncidentStatus.Resolved).ToList();
        return Task.FromResult(open);
    }

    public Task<IReadOnlyList<Incident>> GetAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Incident> all = _incidents.Values.ToList();
        return Task.FromResult(all);
    }

    public void Add(Incident incident) => _incidents[incident.Id] = incident;

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class MutableClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// These orchestration tests care about repository/dedup-store state, not who's listening for
/// notifications — a no-op is the honest fake here, not a spy tracking calls nobody asserts on.
/// </summary>
internal sealed class NoOpPublisher : IPublisher
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;
}
