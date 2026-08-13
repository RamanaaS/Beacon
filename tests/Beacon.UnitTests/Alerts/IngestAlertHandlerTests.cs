using Beacon.Application.Alerts;
using Beacon.Domain.Alerts;
using Beacon.Domain.Common;
using FluentAssertions;

namespace Beacon.UnitTests.Alerts;

public class IngestAlertHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task First_alert_for_a_fingerprint_declares_a_new_incident()
    {
        var repository = new InMemoryIncidentRepository();
        var handler = new IngestAlertCommandHandler(new InMemoryRecentAlertsStore(), repository, new MutableClock(Now));

        var result = await handler.Handle(new IngestAlertCommand(
            "prometheus", "HighCPU", "web-1", Severity.Sev2_High, "CPU usage above threshold"), CancellationToken.None);

        result.Decision.Should().Be(AlertDecision.New);
        result.IncidentId.Should().NotBeNull();

        var incident = await repository.GetByIdAsync(result.IncidentId!.Value);
        incident.Should().NotBeNull();
        incident!.Title.Should().Be("HighCPU on web-1");
        incident.CurrentSeverity.Should().Be(Severity.Sev2_High);
    }

    [Fact]
    public async Task Same_alert_again_inside_the_dedup_window_does_not_declare_a_second_incident()
    {
        var clock = new MutableClock(Now);
        var handler = new IngestAlertCommandHandler(new InMemoryRecentAlertsStore(), new InMemoryIncidentRepository(), clock);
        var command = new IngestAlertCommand("prometheus", "HighCPU", "web-1", Severity.Sev2_High, "CPU usage above threshold");

        var first = await handler.Handle(command, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        var second = await handler.Handle(command, CancellationToken.None);

        first.Decision.Should().Be(AlertDecision.New);
        second.Decision.Should().Be(AlertDecision.Duplicate);
        second.IncidentId.Should().BeNull();
    }

    [Fact]
    public async Task Same_alert_again_outside_the_dedup_window_declares_a_second_incident()
    {
        var clock = new MutableClock(Now);
        var handler = new IngestAlertCommandHandler(new InMemoryRecentAlertsStore(), new InMemoryIncidentRepository(), clock);
        var command = new IngestAlertCommand("prometheus", "HighCPU", "web-1", Severity.Sev2_High, "CPU usage above threshold");

        var first = await handler.Handle(command, CancellationToken.None);
        clock.Advance(IngestAlertCommandHandler.DedupWindow + TimeSpan.FromSeconds(1));
        var second = await handler.Handle(command, CancellationToken.None);

        second.Decision.Should().Be(AlertDecision.New);
        second.IncidentId.Should().NotBe(first.IncidentId);
    }

    [Fact]
    public async Task Different_instance_is_a_different_fingerprint_and_declares_its_own_incident()
    {
        var handler = new IngestAlertCommandHandler(new InMemoryRecentAlertsStore(), new InMemoryIncidentRepository(), new MutableClock(Now));

        var first = await handler.Handle(new IngestAlertCommand(
            "prometheus", "HighCPU", "web-1", Severity.Sev2_High, "CPU usage above threshold"), CancellationToken.None);
        var second = await handler.Handle(new IngestAlertCommand(
            "prometheus", "HighCPU", "web-2", Severity.Sev2_High, "CPU usage above threshold"), CancellationToken.None);

        second.Decision.Should().Be(AlertDecision.New);
        second.IncidentId.Should().NotBe(first.IncidentId);
    }
}
