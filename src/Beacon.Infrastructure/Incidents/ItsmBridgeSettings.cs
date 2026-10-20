namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// No API key here, unlike ClaudeSettings — itsm-bridge is an internal service Beacon itself owns
/// the deployment of (ARCHITECTURE.md's "one deliberate service boundary"), not a third-party
/// vendor API. A real deployment would still want service-to-service auth (mTLS or a shared
/// secret), left out here for the same "genuinely deliberate scope decision" reasoning as every
/// other explicitly-flagged gap in this project, not an oversight.
/// </summary>
public sealed class ItsmBridgeSettings
{
    public string BaseUrl { get; init; } = "http://localhost:8080";

    /// <summary>
    /// False for a deployment with no itsm-bridge at all (the free public demo). Ticket sync
    /// already degrades gracefully when the bridge is down, but only after paying the client's
    /// retry and timeout budget on every declare/resolve. Disabling it skips the call entirely.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
