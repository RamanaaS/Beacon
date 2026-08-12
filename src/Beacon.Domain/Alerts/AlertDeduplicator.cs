namespace Beacon.Domain.Alerts;

public enum AlertDecision
{
    New,
    Duplicate
}

/// <summary>
/// A stateless domain service — this behavior doesn't naturally belong to one entity, so it
/// isn't forced onto Alert as a method. Pure function: given an incoming alert and the recent
/// alerts it should be compared against, decide whether it's new or a duplicate within the
/// window. The caller is responsible for supplying "recent" (Redis-backed, in a later phase) —
/// this type has zero I/O and zero knowledge of where alerts are stored.
/// </summary>
public static class AlertDeduplicator
{
    public static AlertDecision Evaluate(Alert incoming, IEnumerable<Alert> recentAlerts, TimeSpan window)
    {
        var cutoff = incoming.ReceivedAt - window;

        var isDuplicate = recentAlerts.Any(existing =>
            existing.Fingerprint == incoming.Fingerprint &&
            existing.ReceivedAt > cutoff &&
            existing.ReceivedAt <= incoming.ReceivedAt);

        return isDuplicate ? AlertDecision.Duplicate : AlertDecision.New;
    }
}
