using Beacon.Domain.Common;

namespace Beacon.Domain.Alerts;

/// <summary>
/// A single inbound alert. Fingerprint is a stable identifier for "the same underlying
/// condition" (mirrors Prometheus Alertmanager's grouping-key concept) — computing it from
/// raw webhook labels is an Infrastructure concern for a later phase; the domain only needs
/// to know two alerts share a fingerprint, not how that string was derived.
/// </summary>
public sealed record Alert(
    string Fingerprint,
    string Source,
    Severity Severity,
    string Message,
    DateTimeOffset ReceivedAt);
