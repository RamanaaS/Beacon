using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

/// <summary>
/// Pure prompt-construction logic — no I/O, testable without any network call, same reasoning
/// as IncidentEventFormatter (which this reuses directly for the timeline section instead of
/// re-deriving event descriptions a second way). The actual HTTP call to Claude lives entirely
/// in Infrastructure; this class has no idea an LLM is even involved.
/// </summary>
public static class PostmortemPromptBuilder
{
    public static string Build(Incident incident)
    {
        var timeline = string.Join("\n", incident.Events
            .OrderBy(e => e.OccurredAt)
            .Select(e =>
                $"- {e.OccurredAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC " +
                $"[{IncidentEventFormatter.EventTypeLabel(e)}] ({IncidentEventFormatter.Actor(e)}): " +
                $"{IncidentEventFormatter.Describe(e)}"));

        return $"""
            You are drafting an internal incident postmortem for a software engineering team.
            This is a DRAFT for a human engineer to review and edit before it is published —
            do not present it as final, authoritative, or ready to ship as-is.

            Incident: {incident.Title}
            Severity: {incident.CurrentSeverity}
            Status: {incident.CurrentStatus}
            Escalated: {(incident.IsEscalated ? "Yes" : "No")}

            Timeline of recorded events (this is the ONLY source of truth available to you):
            {timeline}

            Write a concise postmortem draft with these sections:
            1. Summary (2-3 sentences)
            2. Root Cause Hypothesis (based only on the timeline above — say plainly if the
               timeline doesn't contain enough information to determine a root cause, rather than
               guessing)
            3. Impact
            4. Timeline Narrative (a readable prose version of the events above)
            5. Suggested Action Items (concrete, actionable follow-ups)

            Stay strictly factual and grounded in the timeline provided. Do not invent details,
            names, or causes that aren't present above.
            """;
    }
}
