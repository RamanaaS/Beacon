using Beacon.Application.Incidents;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using FluentAssertions;

namespace Beacon.UnitTests.Incidents;

public class IncidentEventFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Declared_event_is_labeled_and_attributed_to_the_declarer()
    {
        var e = new IncidentDeclared(Now, "DB down", Severity.Sev1_Critical, "user-1", "fp-1");

        IncidentEventFormatter.EventTypeLabel(e).Should().Be("Declared");
        IncidentEventFormatter.Actor(e).Should().Be("user-1");
        IncidentEventFormatter.Describe(e).Should().Contain("DB down").And.Contain("Sev1_Critical");
    }

    [Fact]
    public void NoteAdded_description_is_the_note_text_verbatim()
    {
        var e = new NoteAdded(Now, "user-2", "Rolled back the last deploy, monitoring now.");

        IncidentEventFormatter.Describe(e).Should().Be("Rolled back the last deploy, monitoring now.");
    }

    [Fact]
    public void SeverityChanged_description_mentions_both_old_and_new_severity()
    {
        var e = new SeverityChanged(Now, "user-3", Severity.Sev3_Medium, Severity.Sev1_Critical);

        IncidentEventFormatter.Describe(e).Should().Contain("Sev3_Medium").And.Contain("Sev1_Critical");
    }

    [Fact]
    public void Resolved_description_includes_the_summary()
    {
        var e = new IncidentResolved(Now, "user-4", "Rolled back the bad deploy.");

        IncidentEventFormatter.EventTypeLabel(e).Should().Be("Resolved");
        IncidentEventFormatter.Describe(e).Should().Contain("Rolled back the bad deploy.");
    }

    [Fact]
    public void Escalated_event_is_attributed_to_the_escalator_not_the_target()
    {
        var e = new IncidentEscalated(Now, "system:escalation-checker", "secondary-oncall");

        IncidentEventFormatter.Actor(e).Should().Be("system:escalation-checker");
        IncidentEventFormatter.Describe(e).Should().Contain("secondary-oncall");
    }
}
