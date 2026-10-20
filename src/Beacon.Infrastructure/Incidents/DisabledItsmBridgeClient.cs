using Beacon.Application.Incidents;

namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// Registered instead of ItsmBridgeClient when "ItsmBridge:Enabled" is false: a deployment with
/// no itsm-bridge process at all, where every call would only ever time out.
/// </summary>
public sealed class DisabledItsmBridgeClient : IItsmBridgeClient
{
    public Task CreateTicketAsync(string incidentId, string title, string severity, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task CloseTicketAsync(string incidentId, CancellationToken ct = default) =>
        Task.CompletedTask;
}
