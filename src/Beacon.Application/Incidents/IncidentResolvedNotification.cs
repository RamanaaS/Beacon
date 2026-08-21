using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

/// <summary>
/// Same pattern as IncidentDeclaredNotification (Phase 11) — Application publishes the fact,
/// Infrastructure's ItsmTicketSyncHandler (Phase 13) reacts to it by closing the corresponding
/// itsm-bridge ticket. ResolveIncidentCommandHandler doesn't know itsm-bridge exists.
/// </summary>
public sealed record IncidentResolvedNotification(IncidentId IncidentId) : INotification;
