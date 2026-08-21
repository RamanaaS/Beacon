using Microsoft.AspNetCore.SignalR;

namespace Beacon.Api.Incidents;

/// <summary>
/// SignalR, deliberately alongside the raw System.Net.WebSockets hub in Beacon.Api.Telemetry
/// (Phase 4) rather than replacing it — see ARCHITECTURE.md Table C for the reasoning. This one
/// backs Beacon.Admin's live incident alerts: broadcast-only, no client-invokable methods, no
/// groups. Where SignalR earns its higher-level abstraction over raw WebSockets is exactly here —
/// a single named event with a typed payload, sent to everyone, with none of the manual framing/
/// connection-bookkeeping Beacon.Api.Telemetry's WebSocketConnectionManager hand-rolls.
/// </summary>
public sealed class IncidentAlertsHub : Hub;
