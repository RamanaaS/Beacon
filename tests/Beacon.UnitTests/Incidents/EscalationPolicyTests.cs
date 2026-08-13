using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using FluentAssertions;

namespace Beacon.UnitTests.Incidents;

public class EscalationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sev1_unacknowledged_past_5_minutes_is_due()
    {
        var incident = Incident.Declare("DB down", Severity.Sev1_Critical, "user-1", "fp-1", Now);

        var due = EscalationPolicy.IsEscalationDue(incident, Now.AddMinutes(5).AddSeconds(1));

        due.Should().BeTrue();
    }

    [Fact]
    public void Sev1_unacknowledged_before_5_minutes_is_not_due_yet()
    {
        var incident = Incident.Declare("DB down", Severity.Sev1_Critical, "user-1", "fp-1", Now);

        var due = EscalationPolicy.IsEscalationDue(incident, Now.AddMinutes(4));

        due.Should().BeFalse();
    }

    [Fact]
    public void Sev2_uses_a_15_minute_threshold_not_5()
    {
        var incident = Incident.Declare("Elevated latency", Severity.Sev2_High, "user-1", "fp-1", Now);

        EscalationPolicy.IsEscalationDue(incident, Now.AddMinutes(6)).Should().BeFalse();
        EscalationPolicy.IsEscalationDue(incident, Now.AddMinutes(15).AddSeconds(1)).Should().BeTrue();
    }

    [Theory]
    [InlineData(Severity.Sev3_Medium)]
    [InlineData(Severity.Sev4_Low)]
    public void Low_severities_never_auto_escalate(Severity severity)
    {
        var incident = Incident.Declare("Minor issue", severity, "user-1", "fp-1", Now);

        var due = EscalationPolicy.IsEscalationDue(incident, Now.AddHours(6));

        due.Should().BeFalse();
    }

    [Fact]
    public void Acknowledged_incidents_are_never_due_regardless_of_age()
    {
        var incident = Incident.Declare("DB down", Severity.Sev1_Critical, "user-1", "fp-1", Now);
        incident.Acknowledge("user-7", Now.AddMinutes(1));

        var due = EscalationPolicy.IsEscalationDue(incident, Now.AddHours(1));

        due.Should().BeFalse();
    }

    [Fact]
    public void Already_escalated_incidents_are_not_due_again()
    {
        var incident = Incident.Declare("DB down", Severity.Sev1_Critical, "user-1", "fp-1", Now);
        incident.Escalate("system", "secondary-oncall", Now.AddMinutes(5));

        var due = EscalationPolicy.IsEscalationDue(incident, Now.AddMinutes(30));

        due.Should().BeFalse();
    }
}
