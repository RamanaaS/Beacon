using Beacon.Application.Common;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

public enum SystemStatus
{
    Operational,
    DegradedPerformance,
    PartialOutage,
    MajorOutage
}

public sealed record PublicComponentStatus(string Name, string Status);

/// <summary>
/// Deliberately narrower than IncidentSummaryResponse (Phase 9's ListIncidentsQuery): no actor
/// ids, no internal notes, nothing that identifies who's working the incident. This is the one
/// endpoint in the system a customer with no account can reach — it shows only what a status page
/// should ever show a stranger.
/// </summary>
public sealed record PublicIncidentSummary(string Title, Severity Severity, IncidentStatus Status, DateTimeOffset DeclaredAt);

public sealed record UptimeDay(DateOnly Date, bool HadMajorIncident);

public sealed record PublicStatusResponse(
    SystemStatus OverallStatus,
    IReadOnlyList<PublicComponentStatus> Components,
    IReadOnlyList<PublicIncidentSummary> ActiveIncidents,
    IReadOnlyList<UptimeDay> UptimeHistory);

public sealed record GetPublicStatusQuery : IRequest<PublicStatusResponse>;

/// <summary>
/// Component health always reads "Operational" for now — Incident has no per-component tag (no
/// phase through Phase 11 needed one), so there's no real signal to compute per-service health
/// from. A real status page ties each incident to the specific component(s) it affects; adding
/// that here would mean touching the aggregate, the declare command, every existing caller across
/// three frontends, and a migration, for a demo page. Documented as an explicit, deliberate scope
/// boundary — same pattern as the Phase 9 raw-GUID-actor gap — not a silently missing feature.
/// The overall banner and uptime history are still real signals, computed from actual incident
/// severity and duration, not hardcoded.
/// </summary>
public sealed class GetPublicStatusQueryHandler(IIncidentRepository repository, IClock clock)
    : IRequestHandler<GetPublicStatusQuery, PublicStatusResponse>
{
    private static readonly string[] MonitoredComponents = ["payments-api", "checkout-api", "auth-api"];
    private const int UptimeWindowDays = 30;

    public async Task<PublicStatusResponse> Handle(GetPublicStatusQuery request, CancellationToken cancellationToken)
    {
        var incidents = await repository.GetAllAsync(cancellationToken);
        var active = incidents.Where(i => i.CurrentStatus != IncidentStatus.Resolved).ToList();

        var overallStatus = active.Count == 0
            ? SystemStatus.Operational
            : active.Max(i => i.CurrentSeverity) switch
            {
                Severity.Sev1_Critical => SystemStatus.MajorOutage,
                Severity.Sev2_High => SystemStatus.PartialOutage,
                _ => SystemStatus.DegradedPerformance
            };

        var components = MonitoredComponents
            .Select(name => new PublicComponentStatus(name, "Operational"))
            .ToList();

        var activeIncidents = active
            .OrderByDescending(i => i.DeclaredAt)
            .Select(i => new PublicIncidentSummary(i.Title, i.CurrentSeverity, i.CurrentStatus, i.DeclaredAt))
            .ToList();

        // Through IClock like every other handler, not DateTimeOffset.UtcNow: the uptime window is
        // relative to "today", so reading the real clock here made the unit tests (pinned to a
        // fixed date) start failing once that date slid out of the 30-day window.
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var uptimeHistory = Enumerable.Range(0, UptimeWindowDays)
            .Select(daysAgo => today.AddDays(-(UptimeWindowDays - 1 - daysAgo)))
            .Select(date => new UptimeDay(date, HadMajorIncidentOn(incidents, date)))
            .ToList();

        return new PublicStatusResponse(overallStatus, components, activeIncidents, uptimeHistory);
    }

    /// <summary>
    /// "Major" here means Sev1/Sev2 — the same threshold the overall banner uses — and an
    /// incident counts against a given day if it was open (declared, and either still open or not
    /// resolved until on/after that day) at any point during that day's UTC window.
    /// </summary>
    private static bool HadMajorIncidentOn(IReadOnlyList<Incident> incidents, DateOnly date)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        return incidents.Any(i =>
            i.CurrentSeverity is Severity.Sev1_Critical or Severity.Sev2_High &&
            i.DeclaredAt.UtcDateTime < dayEnd &&
            (ResolvedAt(i) is not { } resolvedAt || resolvedAt.UtcDateTime >= dayStart));
    }

    private static DateTimeOffset? ResolvedAt(Incident incident) =>
        incident.Events.OfType<IncidentResolved>()
            .Select(e => (DateTimeOffset?)e.OccurredAt)
            .FirstOrDefault();
}
