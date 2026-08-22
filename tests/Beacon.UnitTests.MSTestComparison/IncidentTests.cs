using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Beacon.UnitTests.MSTestComparison;

/// <summary>
/// A direct MSTest port of Beacon.UnitTests/Incidents/IncidentTests.cs — same 14 cases, same
/// domain logic under test, deliberately different test framework. This is the resume-parity
/// suite ARCHITECTURE.md's Table C calls for: proof of MSTest fluency, kept honest rather than
/// silently dropped once xUnit+FluentAssertions was chosen as the primary suite. Reading the two
/// side by side is the actual point — [TestClass]/[TestMethod]/Assert.* here versus
/// bare classes/[Fact]/FluentAssertions' `.Should()` chains there, same coverage either way.
/// </summary>
[TestClass]
public class IncidentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Declare_creates_incident_with_a_single_IncidentDeclared_event()
    {
        var incident = Incident.Declare(
            title: "Payments API returning 500s",
            severity: Severity.Sev1_Critical,
            declaredByUserId: "user-42",
            alertFingerprint: "fp-abc123",
            occurredAt: Now);

        Assert.AreEqual("Payments API returning 500s", incident.Title);
        Assert.AreEqual(Severity.Sev1_Critical, incident.CurrentSeverity);
        Assert.AreEqual(IncidentStatus.Declared, incident.CurrentStatus);
        Assert.AreEqual(1, incident.Events.Count);
        Assert.IsInstanceOfType<IncidentDeclared>(incident.Events.Single());
    }

    [TestMethod]
    public void Declare_rejects_an_empty_title()
    {
        Assert.ThrowsException<ArgumentException>(() => Incident.Declare("", Severity.Sev2_High, "user-1", "fp-1", Now));
    }

    [TestMethod]
    public void Acknowledge_moves_status_to_Acknowledged_and_appends_one_event()
    {
        var incident = Declared();

        incident.Acknowledge("user-7", Now.AddMinutes(2));

        Assert.AreEqual(IncidentStatus.Acknowledged, incident.CurrentStatus);
        Assert.AreEqual(2, incident.Events.Count);
    }

    [TestMethod]
    public void ChangeSeverity_records_old_and_new_severity()
    {
        var incident = Declared(Severity.Sev3_Medium);

        incident.ChangeSeverity("user-7", Severity.Sev1_Critical, Now.AddMinutes(5));

        Assert.AreEqual(Severity.Sev1_Critical, incident.CurrentSeverity);
        var change = incident.Events.OfType<SeverityChanged>().Single();
        Assert.AreEqual(Severity.Sev3_Medium, change.OldSeverity);
        Assert.AreEqual(Severity.Sev1_Critical, change.NewSeverity);
    }

    [TestMethod]
    public void ChangeSeverity_to_the_same_value_is_a_no_op()
    {
        var incident = Declared(Severity.Sev2_High);

        incident.ChangeSeverity("user-7", Severity.Sev2_High, Now.AddMinutes(5));

        Assert.AreEqual(1, incident.Events.Count); // no SeverityChanged event appended
    }

    [TestMethod]
    public void Multiple_responders_can_join_and_are_listed_without_duplicates()
    {
        var incident = Declared();

        incident.AddResponder("user-7", "Commander", Now.AddMinutes(1));
        incident.AddResponder("user-8", "Scribe", Now.AddMinutes(2));
        incident.AddResponder("user-7", "Commander", Now.AddMinutes(3)); // rejoins

        CollectionAssert.AreEquivalent(new[] { "user-7", "user-8" }, incident.Responders.ToList());
    }

    [TestMethod]
    public void Resolve_requires_a_summary()
    {
        var incident = Declared();

        Assert.ThrowsException<ArgumentException>(() => incident.Resolve("user-7", "", Now.AddMinutes(10)));
    }

    [TestMethod]
    public void Resolve_sets_status_to_Resolved()
    {
        var incident = Declared();

        incident.Resolve("user-7", "Rolled back the bad deploy.", Now.AddMinutes(10));

        Assert.AreEqual(IncidentStatus.Resolved, incident.CurrentStatus);
    }

    [TestMethod]
    public void A_resolved_incident_rejects_further_changes()
    {
        var incident = Declared();
        incident.Resolve("user-7", "Rolled back the bad deploy.", Now.AddMinutes(10));

        Assert.ThrowsException<InvalidOperationException>(() => incident.AddNote("user-8", "one more thing", Now.AddMinutes(11)));
    }

    [TestMethod]
    public void Escalate_appends_an_IncidentEscalated_event_and_sets_IsEscalated()
    {
        var incident = Declared();

        incident.Escalate("system", "secondary-oncall", Now.AddMinutes(5));

        Assert.IsTrue(incident.IsEscalated);
        Assert.AreEqual("secondary-oncall", incident.Events.OfType<IncidentEscalated>().Single().EscalatedTo);
    }

    [TestMethod]
    public void Escalate_requires_a_target()
    {
        var incident = Declared();

        Assert.ThrowsException<ArgumentException>(() => incident.Escalate("system", "", Now.AddMinutes(5)));
    }

    [TestMethod]
    public void A_resolved_incident_rejects_escalation_too()
    {
        var incident = Declared();
        incident.Resolve("user-7", "fixed", Now.AddMinutes(10));

        Assert.ThrowsException<InvalidOperationException>(() => incident.Escalate("system", "secondary-oncall", Now.AddMinutes(11)));
    }

    [TestMethod]
    public void Rehydrate_reconstructs_current_state_purely_from_history()
    {
        var original = Declared(Severity.Sev3_Medium);
        original.Acknowledge("user-7", Now.AddMinutes(2));
        original.ChangeSeverity("user-7", Severity.Sev1_Critical, Now.AddMinutes(4));

        var rehydrated = Incident.Rehydrate(original.Id, original.Events);

        Assert.AreEqual(IncidentStatus.Acknowledged, rehydrated.CurrentStatus);
        Assert.AreEqual(Severity.Sev1_Critical, rehydrated.CurrentSeverity);
    }

    private static Incident Declared(Severity severity = Severity.Sev2_High) =>
        Incident.Declare("Test incident", severity, "user-1", "fp-1", Now);
}
