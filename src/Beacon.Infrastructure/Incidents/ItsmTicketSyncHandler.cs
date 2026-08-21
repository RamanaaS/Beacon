using Beacon.Application.Incidents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// Bridges Beacon's own MediatR notifications to itsm-bridge. Deliberately swallows every
/// failure here rather than letting one propagate: MediatR's default publisher awaits every
/// registered INotificationHandler for a given notification, and an unhandled exception from any
/// one of them propagates back out of IPublisher.Publish to the command handler that called it —
/// which in DeclareIncidentCommandHandler/ResolveIncidentCommandHandler runs *after*
/// SaveChangesAsync. A downed itsm-bridge throwing here would mean the incident was genuinely
/// declared/resolved in the database, but the caller sees a failed request — the exact same class
/// of bug this project already found and fixed once this phase (RedisRecentAlertsStore's original
/// unguarded Redis calls). Applied proactively here instead of discovered the same way twice.
/// </summary>
public sealed class ItsmTicketSyncHandler(IItsmBridgeClient itsmBridge, ILogger<ItsmTicketSyncHandler> logger)
    : INotificationHandler<IncidentDeclaredNotification>, INotificationHandler<IncidentResolvedNotification>
{
    public async Task Handle(IncidentDeclaredNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await itsmBridge.CreateTicketAsync(
                notification.IncidentId.Value.ToString(), notification.Title, notification.Severity.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "itsm-bridge unreachable — could not create a ticket for incident {IncidentId}", notification.IncidentId);
        }
    }

    public async Task Handle(IncidentResolvedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await itsmBridge.CloseTicketAsync(notification.IncidentId.Value.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "itsm-bridge unreachable — could not close the ticket for incident {IncidentId}", notification.IncidentId);
        }
    }
}
