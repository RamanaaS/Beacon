using Beacon.Application.Common;
using Beacon.Application.Incidents;
using Beacon.Domain.Alerts;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Alerts;

public sealed record IngestAlertCommand(
    string Source,
    string AlertName,
    string Instance,
    Severity Severity,
    string Message) : IRequest<IngestAlertResult>;

public sealed record IngestAlertResult(AlertDecision Decision, IncidentId? IncidentId);

/// <summary>
/// Ties together three things built in earlier phases: the Fingerprint concept and
/// AlertDeduplicator domain service (Phase 1), IIncidentRepository/IClock (Phase 2's seams), and
/// Incident.Declare (Phase 1's aggregate factory). Nothing here is new domain logic — this
/// handler is orchestration only, which is exactly what the Application layer is for.
/// </summary>
public sealed class IngestAlertCommandHandler(
    IRecentAlertsStore recentAlertsStore,
    IIncidentRepository incidentRepository,
    IClock clock,
    IPublisher publisher) : IRequestHandler<IngestAlertCommand, IngestAlertResult>
{
    // Mirrors Prometheus Alertmanager's default grouping/repeat-interval scale. A real system
    // would make this configurable per alert source; hardcoded is the honest amount of
    // complexity for one alert source and no configuration story yet.
    public static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(5);

    public async Task<IngestAlertResult> Handle(IngestAlertCommand request, CancellationToken cancellationToken)
    {
        var fingerprint = $"{request.AlertName}:{request.Instance}";
        var incoming = new Alert(fingerprint, request.Source, request.Severity, request.Message, clock.UtcNow);

        var recent = await recentAlertsStore.GetRecentAsync(fingerprint, DedupWindow, cancellationToken);
        var decision = AlertDeduplicator.Evaluate(incoming, recent, DedupWindow);

        await recentAlertsStore.RecordAsync(incoming, DedupWindow, cancellationToken);

        if (decision == AlertDecision.Duplicate)
            return new IngestAlertResult(AlertDecision.Duplicate, IncidentId: null);

        var incident = Incident.Declare(
            title: $"{request.AlertName} on {request.Instance}",
            severity: request.Severity,
            declaredByUserId: "system:alert-ingestion",
            alertFingerprint: fingerprint,
            occurredAt: clock.UtcNow);

        incidentRepository.Add(incident);
        await incidentRepository.SaveChangesAsync(cancellationToken);

        await publisher.Publish(
            new IncidentDeclaredNotification(incident.Id, incident.Title, incident.CurrentSeverity),
            cancellationToken);

        return new IngestAlertResult(AlertDecision.New, incident.Id);
    }
}
