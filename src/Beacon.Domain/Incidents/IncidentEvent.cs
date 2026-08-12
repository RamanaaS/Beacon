using Beacon.Domain.Common;

namespace Beacon.Domain.Incidents;

/// <summary>
/// Base type for every fact that can happen to an Incident. Instances are immutable and,
/// once appended, are never edited or removed — see ARCHITECTURE.md Section 4 for why the
/// incident timeline is modeled as an append-only log instead of a mutable status column.
/// </summary>
public abstract record IncidentEvent
{
    public DateTimeOffset OccurredAt { get; }

    protected IncidentEvent(DateTimeOffset occurredAt)
    {
        OccurredAt = occurredAt;
    }
}

public sealed record IncidentDeclared(
    DateTimeOffset OccurredAt,
    string Title,
    Severity Severity,
    string DeclaredByUserId,
    string AlertFingerprint
) : IncidentEvent(OccurredAt);

public sealed record ResponderJoined(
    DateTimeOffset OccurredAt,
    string UserId,
    string Role
) : IncidentEvent(OccurredAt);

public sealed record NoteAdded(
    DateTimeOffset OccurredAt,
    string UserId,
    string Text
) : IncidentEvent(OccurredAt);

public sealed record SeverityChanged(
    DateTimeOffset OccurredAt,
    string UserId,
    Severity OldSeverity,
    Severity NewSeverity
) : IncidentEvent(OccurredAt);

public sealed record IncidentAcknowledged(
    DateTimeOffset OccurredAt,
    string UserId
) : IncidentEvent(OccurredAt);

public sealed record IncidentResolved(
    DateTimeOffset OccurredAt,
    string UserId,
    string Summary
) : IncidentEvent(OccurredAt);
