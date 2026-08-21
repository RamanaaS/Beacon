using Beacon.Application.Incidents;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace Beacon.Api.Incidents;

/// <summary>
/// The bridge between Application's pub/sub (IncidentDeclaredNotification) and SignalR — the one
/// place in the whole codebase that knows both exist. Application never references SignalR;
/// SignalR's hub never references Application's command handlers directly. A MediatR
/// notification handler is the seam that connects them without either side depending on the
/// other's internals.
/// </summary>
public sealed class IncidentDeclaredNotificationHandler(IHubContext<IncidentAlertsHub> hub)
    : INotificationHandler<IncidentDeclaredNotification>
{
    public Task Handle(IncidentDeclaredNotification notification, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(
            "IncidentDeclared",
            // Plain Guid/string, not the domain types directly — SignalR's JSON hub protocol has
            // its own serializer configuration, entirely separate from ConfigureHttpJsonOptions
            // in Program.cs. Sending IncidentId/Severity as-is would hit the exact bug Phase 2
            // found for HTTP responses (IncidentId serializing as {"value": "..."} instead of a
            // plain string) again here, since the converters registered for the HTTP pipeline
            // don't automatically apply to SignalR. Sidestepped entirely by sending primitives.
            notification.IncidentId.Value,
            notification.Title,
            notification.Severity.ToString(),
            cancellationToken);
}
