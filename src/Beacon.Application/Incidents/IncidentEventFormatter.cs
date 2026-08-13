using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

/// <summary>
/// Pure formatting logic shared by the Excel and PDF report generators — "how do we describe
/// this event to a human reading a postmortem" is a presentation concern, not a domain rule, so
/// it lives here rather than on IncidentEvent itself. No I/O, same reasoning as
/// AlertDeduplicator/EscalationPolicy for why this is a plain static class, not a service with
/// injected dependencies.
/// </summary>
public static class IncidentEventFormatter
{
    public static string EventTypeLabel(IncidentEvent @event) => @event switch
    {
        IncidentDeclared => "Declared",
        ResponderJoined => "Responder Joined",
        NoteAdded => "Note",
        SeverityChanged => "Severity Changed",
        IncidentAcknowledged => "Acknowledged",
        IncidentResolved => "Resolved",
        IncidentEscalated => "Escalated",
        _ => @event.GetType().Name
    };

    public static string Actor(IncidentEvent @event) => @event switch
    {
        IncidentDeclared e => e.DeclaredByUserId,
        ResponderJoined e => e.UserId,
        NoteAdded e => e.UserId,
        SeverityChanged e => e.UserId,
        IncidentAcknowledged e => e.UserId,
        IncidentResolved e => e.UserId,
        IncidentEscalated e => e.EscalatedByUserId,
        _ => "unknown"
    };

    public static string Describe(IncidentEvent @event) => @event switch
    {
        IncidentDeclared e => $"Declared \"{e.Title}\" at {e.Severity} (alert fingerprint {e.AlertFingerprint})",
        ResponderJoined e => $"Joined as {e.Role}",
        NoteAdded e => e.Text,
        SeverityChanged e => $"Severity changed from {e.OldSeverity} to {e.NewSeverity}",
        IncidentAcknowledged => "Acknowledged the incident",
        IncidentResolved e => $"Resolved: {e.Summary}",
        IncidentEscalated e => $"Escalated to {e.EscalatedTo}",
        _ => string.Empty
    };
}
