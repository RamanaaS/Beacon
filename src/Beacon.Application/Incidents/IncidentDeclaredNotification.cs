using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

/// <summary>
/// The pub/sub side of MediatR, distinct from the request/response commands used everywhere
/// else — Application publishes a fact ("this happened"), and doesn't know or care who's
/// listening. Beacon.Api has a handler that bridges this to SignalR (Beacon.Admin's live alerts);
/// Application itself never references SignalR, keeping the same layering discipline as every
/// other seam in this project.
/// </summary>
public sealed record IncidentDeclaredNotification(IncidentId IncidentId, string Title, Severity Severity) : INotification;
