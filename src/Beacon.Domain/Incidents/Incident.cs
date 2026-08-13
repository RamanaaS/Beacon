using Beacon.Domain.Common;

namespace Beacon.Domain.Incidents;

/// <summary>
/// Aggregate root. All state changes go through a behavior method (Declare, Acknowledge,
/// AddNote, ...) that validates a business rule and appends an IncidentEvent — there are no
/// public setters. Everything callers read (Title, CurrentSeverity, CurrentStatus, Responders)
/// is a projection computed from the event list, not stored redundantly.
/// </summary>
public sealed class Incident
{
    private readonly List<IncidentEvent> _events = new();

    public IncidentId Id { get; }
    public IReadOnlyList<IncidentEvent> Events => _events;

    private Incident(IncidentId id)
    {
        Id = id;
    }

    public static Incident Declare(
        string title,
        Severity severity,
        string declaredByUserId,
        string alertFingerprint,
        DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Incident title cannot be empty.", nameof(title));

        var incident = new Incident(IncidentId.New());
        incident.Raise(new IncidentDeclared(occurredAt, title, severity, declaredByUserId, alertFingerprint));
        return incident;
    }

    /// <summary>
    /// Rebuilds an Incident purely from its history — no field is ever read from a
    /// separate "current state" record. This is what a repository implementation
    /// will call once EF Core is wired up in a later phase.
    /// </summary>
    public static Incident Rehydrate(IncidentId id, IEnumerable<IncidentEvent> history)
    {
        var incident = new Incident(id);
        incident._events.AddRange(history.OrderBy(e => e.OccurredAt));
        return incident;
    }

    public void Acknowledge(string userId, DateTimeOffset occurredAt)
    {
        EnsureNotResolved();
        Raise(new IncidentAcknowledged(occurredAt, userId));
    }

    public void AddResponder(string userId, string role, DateTimeOffset occurredAt)
    {
        EnsureNotResolved();
        Raise(new ResponderJoined(occurredAt, userId, role));
    }

    public void AddNote(string userId, string text, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Note text cannot be empty.", nameof(text));

        EnsureNotResolved();
        Raise(new NoteAdded(occurredAt, userId, text));
    }

    public void ChangeSeverity(string userId, Severity newSeverity, DateTimeOffset occurredAt)
    {
        EnsureNotResolved();

        if (newSeverity == CurrentSeverity)
            return; // same value twice isn't a fact worth logging — not an error either

        Raise(new SeverityChanged(occurredAt, userId, CurrentSeverity, newSeverity));
    }

    public void Resolve(string userId, string summary, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("A resolved incident requires a summary.", nameof(summary));

        EnsureNotResolved();
        Raise(new IncidentResolved(occurredAt, userId, summary));
    }

    public void Escalate(string escalatedByUserId, string escalatedTo, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(escalatedTo))
            throw new ArgumentException("Escalation requires a target.", nameof(escalatedTo));

        EnsureNotResolved();
        Raise(new IncidentEscalated(occurredAt, escalatedByUserId, escalatedTo));
    }

    public string Title => _events.OfType<IncidentDeclared>().Single().Title;

    public Severity CurrentSeverity =>
        _events.OfType<SeverityChanged>()
               .Select(e => e.NewSeverity)
               .DefaultIfEmpty(_events.OfType<IncidentDeclared>().Single().Severity)
               .Last();

    public IncidentStatus CurrentStatus => _events switch
    {
        _ when _events.OfType<IncidentResolved>().Any() => IncidentStatus.Resolved,
        _ when _events.OfType<IncidentAcknowledged>().Any() => IncidentStatus.Acknowledged,
        _ => IncidentStatus.Declared
    };

    public IReadOnlyList<string> Responders =>
        _events.OfType<ResponderJoined>().Select(e => e.UserId).Distinct().ToList();

    public bool IsEscalated => _events.OfType<IncidentEscalated>().Any();

    public DateTimeOffset DeclaredAt => _events.OfType<IncidentDeclared>().Single().OccurredAt;

    private void EnsureNotResolved()
    {
        if (CurrentStatus == IncidentStatus.Resolved)
            throw new InvalidOperationException($"Incident {Id} is already resolved and cannot be modified.");
    }

    private void Raise(IncidentEvent @event) => _events.Add(@event);
}
