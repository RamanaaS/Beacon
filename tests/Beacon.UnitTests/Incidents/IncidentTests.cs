using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using FluentAssertions;

namespace Beacon.UnitTests.Incidents;

public class IncidentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Declare_creates_incident_with_a_single_IncidentDeclared_event()
    {
        var incident = Incident.Declare(
            title: "Payments API returning 500s",
            severity: Severity.Sev1_Critical,
            declaredByUserId: "user-42",
            alertFingerprint: "fp-abc123",
            occurredAt: Now);

        incident.Title.Should().Be("Payments API returning 500s");
        incident.CurrentSeverity.Should().Be(Severity.Sev1_Critical);
        incident.CurrentStatus.Should().Be(IncidentStatus.Declared);
        incident.Events.Should().ContainSingle().Which.Should().BeOfType<IncidentDeclared>();
    }

    [Fact]
    public void Declare_rejects_an_empty_title()
    {
        var act = () => Incident.Declare("", Severity.Sev2_High, "user-1", "fp-1", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Acknowledge_moves_status_to_Acknowledged_and_appends_one_event()
    {
        var incident = Declared();

        incident.Acknowledge("user-7", Now.AddMinutes(2));

        incident.CurrentStatus.Should().Be(IncidentStatus.Acknowledged);
        incident.Events.Should().HaveCount(2);
    }

    [Fact]
    public void ChangeSeverity_records_old_and_new_severity()
    {
        var incident = Declared(severity: Severity.Sev3_Medium);

        incident.ChangeSeverity("user-7", Severity.Sev1_Critical, Now.AddMinutes(5));

        incident.CurrentSeverity.Should().Be(Severity.Sev1_Critical);
        var change = incident.Events.OfType<SeverityChanged>().Single();
        change.OldSeverity.Should().Be(Severity.Sev3_Medium);
        change.NewSeverity.Should().Be(Severity.Sev1_Critical);
    }

    [Fact]
    public void ChangeSeverity_to_the_same_value_is_a_no_op()
    {
        var incident = Declared(severity: Severity.Sev2_High);

        incident.ChangeSeverity("user-7", Severity.Sev2_High, Now.AddMinutes(5));

        incident.Events.Should().ContainSingle(); // no SeverityChanged event appended
    }

    [Fact]
    public void Multiple_responders_can_join_and_are_listed_without_duplicates()
    {
        var incident = Declared();

        incident.AddResponder("user-7", "Commander", Now.AddMinutes(1));
        incident.AddResponder("user-8", "Scribe", Now.AddMinutes(2));
        incident.AddResponder("user-7", "Commander", Now.AddMinutes(3)); // rejoins

        incident.Responders.Should().BeEquivalentTo("user-7", "user-8");
    }

    [Fact]
    public void Resolve_requires_a_summary()
    {
        var incident = Declared();

        var act = () => incident.Resolve("user-7", "", Now.AddMinutes(10));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_sets_status_to_Resolved()
    {
        var incident = Declared();

        incident.Resolve("user-7", "Rolled back the bad deploy.", Now.AddMinutes(10));

        incident.CurrentStatus.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public void A_resolved_incident_rejects_further_changes()
    {
        var incident = Declared();
        incident.Resolve("user-7", "Rolled back the bad deploy.", Now.AddMinutes(10));

        var act = () => incident.AddNote("user-8", "one more thing", Now.AddMinutes(11));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_reconstructs_current_state_purely_from_history()
    {
        var original = Declared(severity: Severity.Sev3_Medium);
        original.Acknowledge("user-7", Now.AddMinutes(2));
        original.ChangeSeverity("user-7", Severity.Sev1_Critical, Now.AddMinutes(4));

        var rehydrated = Incident.Rehydrate(original.Id, original.Events);

        rehydrated.CurrentStatus.Should().Be(IncidentStatus.Acknowledged);
        rehydrated.CurrentSeverity.Should().Be(Severity.Sev1_Critical);
    }

    private static Incident Declared(Severity severity = Severity.Sev2_High) =>
        Incident.Declare("Test incident", severity, "user-1", "fp-1", Now);
}
