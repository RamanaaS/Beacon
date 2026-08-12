using Beacon.Application.Common;
using Beacon.Application.Incidents;
using Beacon.Domain.Alerts;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;

namespace Beacon.Application.Alerts;

public sealed record IngestAlertRequest(
    string Source,
    string AlertName,
    string Instance,
    Severity Severity,
    string Message);

public sealed record IngestAlertResult(AlertDecision Decision, IncidentId? IncidentId);

/// <summary>
/// Ties together three things built in earlier phases: the Fingerprint concept and
/// AlertDeduplicator domain service (Phase 1), IIncidentRepository/IClock (Phase 2's seams), and
/// Incident.Declare (Phase 1's aggregate factory). Nothing here is new domain logic — this
/// handler is orchestration only, which is exactly what the Application layer is for.
/// </summary>
public sealed class IngestAlertHandler(
    IRecentAlertsStore recentAlertsStore,
    IIncidentRepository incidentRepository,
    IClock clock)
{
    // Mirrors Prometheus Alertmanager's default grouping/repeat-interval scale. A real system
    // would make this configurable per alert source; hardcoded is the honest amount of
    // complexity for one alert source and no configuration story yet.
    public static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(5);

    public async Task<IngestAlertResult> HandleAsync(IngestAlertRequest request, CancellationToken ct = default)
    {
        var fingerprint = $"{request.AlertName}:{request.Instance}";
        var incoming = new Alert(fingerprint, request.Source, request.Severity, request.Message, clock.UtcNow);

        var recent = await recentAlertsStore.GetRecentAsync(fingerprint, DedupWindow, ct);
        var decision = AlertDeduplicator.Evaluate(incoming, recent, DedupWindow);

        await recentAlertsStore.RecordAsync(incoming, DedupWindow, ct);

        if (decision == AlertDecision.Duplicate)
            return new IngestAlertResult(AlertDecision.Duplicate, IncidentId: null);

        var incident = Incident.Declare(
            title: $"{request.AlertName} on {request.Instance}",
            severity: request.Severity,
            declaredByUserId: "system:alert-ingestion",
            alertFingerprint: fingerprint,
            occurredAt: clock.UtcNow);

        incidentRepository.Add(incident);
        await incidentRepository.SaveChangesAsync(ct);

        return new IngestAlertResult(AlertDecision.New, incident.Id);
    }
}
