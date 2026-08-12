using Beacon.Domain.Alerts;
using Beacon.Domain.Common;
using FluentAssertions;

namespace Beacon.UnitTests.Alerts;

public class AlertDeduplicatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    [Fact]
    public void First_alert_with_a_fingerprint_is_New()
    {
        var incoming = MakeAlert("fp-cpu-high", Now);

        var decision = AlertDeduplicator.Evaluate(incoming, recentAlerts: Array.Empty<Alert>(), Window);

        decision.Should().Be(AlertDecision.New);
    }

    [Fact]
    public void Same_fingerprint_inside_the_window_is_a_Duplicate()
    {
        var first = MakeAlert("fp-cpu-high", Now);
        var second = MakeAlert("fp-cpu-high", Now.AddMinutes(2));

        var decision = AlertDeduplicator.Evaluate(second, recentAlerts: [first], Window);

        decision.Should().Be(AlertDecision.Duplicate);
    }

    [Fact]
    public void Same_fingerprint_outside_the_window_is_New_again()
    {
        var first = MakeAlert("fp-cpu-high", Now);
        var second = MakeAlert("fp-cpu-high", Now.AddMinutes(6));

        var decision = AlertDeduplicator.Evaluate(second, recentAlerts: [first], Window);

        decision.Should().Be(AlertDecision.New);
    }

    [Fact]
    public void Different_fingerprint_inside_the_window_is_New()
    {
        var first = MakeAlert("fp-cpu-high", Now);
        var second = MakeAlert("fp-disk-full", Now.AddMinutes(1));

        var decision = AlertDeduplicator.Evaluate(second, recentAlerts: [first], Window);

        decision.Should().Be(AlertDecision.New);
    }

    private static Alert MakeAlert(string fingerprint, DateTimeOffset receivedAt) =>
        new(fingerprint, "prometheus", Severity.Sev2_High, "CPU usage above threshold", receivedAt);
}
