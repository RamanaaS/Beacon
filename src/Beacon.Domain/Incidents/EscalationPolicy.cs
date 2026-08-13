using Beacon.Domain.Common;

namespace Beacon.Domain.Incidents;

/// <summary>
/// A pure domain service, same shape as Alerts.AlertDeduplicator from Phase 1: no I/O, no
/// knowledge of how incidents get fetched or how the decision gets acted on — just "given this
/// incident's state, is escalation due right now." The caller (a background worker, in Phase 5)
/// owns finding candidate incidents and acting on the answer.
/// </summary>
public static class EscalationPolicy
{
    public static bool IsEscalationDue(Incident incident, DateTimeOffset now)
    {
        if (incident.CurrentStatus != IncidentStatus.Declared)
            return false; // already acknowledged or resolved — someone's already on it

        if (incident.IsEscalated)
            return false; // don't re-escalate every time the checker runs

        var threshold = ThresholdFor(incident.CurrentSeverity);
        if (threshold is null)
            return false; // Sev3/Sev4 don't auto-escalate — not urgent enough to page someone over

        return now - incident.DeclaredAt >= threshold;
    }

    private static TimeSpan? ThresholdFor(Severity severity) => severity switch
    {
        Severity.Sev1_Critical => TimeSpan.FromMinutes(5),
        Severity.Sev2_High => TimeSpan.FromMinutes(15),
        _ => null
    };
}
