using Beacon.Domain.Alerts;

namespace Beacon.Application.Alerts;

/// <summary>
/// Backs AlertDeduplicator (Phase 1, Beacon.Domain) with a real time-windowed store. The domain
/// service stays a pure function that takes "recent alerts" as a parameter — this interface is
/// what supplies that parameter for real, and Infrastructure owns how "recent" is actually stored
/// (Redis in production; an in-memory list in tests — see Beacon.UnitTests.Alerts).
/// </summary>
public interface IRecentAlertsStore
{
    Task<IReadOnlyList<Alert>> GetRecentAsync(string fingerprint, TimeSpan window, CancellationToken ct = default);

    Task RecordAsync(Alert alert, TimeSpan retention, CancellationToken ct = default);
}
