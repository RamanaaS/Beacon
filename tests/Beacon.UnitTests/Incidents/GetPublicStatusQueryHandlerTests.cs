using Beacon.Application.Incidents;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using Beacon.UnitTests.Alerts;
using FluentAssertions;

namespace Beacon.UnitTests.Incidents;

public class GetPublicStatusQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_active_incidents_reports_operational()
    {
        var repository = new InMemoryIncidentRepository();
        var incident = Incident.Declare("Past blip", Severity.Sev1_Critical, "user-1", "fp-1", Now.AddDays(-5));
        incident.Resolve("user-1", "Fixed.", Now.AddDays(-5).AddMinutes(10));
        repository.Add(incident);

        var handler = new GetPublicStatusQueryHandler(repository);

        var result = await handler.Handle(new GetPublicStatusQuery(), CancellationToken.None);

        result.OverallStatus.Should().Be(SystemStatus.Operational);
        result.ActiveIncidents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Severity.Sev1_Critical, SystemStatus.MajorOutage)]
    [InlineData(Severity.Sev2_High, SystemStatus.PartialOutage)]
    [InlineData(Severity.Sev3_Medium, SystemStatus.DegradedPerformance)]
    [InlineData(Severity.Sev4_Low, SystemStatus.DegradedPerformance)]
    public async Task Overall_status_reflects_the_highest_severity_active_incident(Severity severity, SystemStatus expected)
    {
        var repository = new InMemoryIncidentRepository();
        repository.Add(Incident.Declare("Ongoing", severity, "user-1", "fp-1", Now));

        var handler = new GetPublicStatusQueryHandler(repository);

        var result = await handler.Handle(new GetPublicStatusQuery(), CancellationToken.None);

        result.OverallStatus.Should().Be(expected);
    }

    [Fact]
    public async Task Active_incidents_never_leak_the_declaring_users_id()
    {
        var repository = new InMemoryIncidentRepository();
        repository.Add(Incident.Declare("Payments down", Severity.Sev1_Critical, "internal-user-guid", "fp-1", Now));

        var handler = new GetPublicStatusQueryHandler(repository);

        var result = await handler.Handle(new GetPublicStatusQuery(), CancellationToken.None);

        result.ActiveIncidents.Should().ContainSingle(i => i.Title == "Payments down");
        // PublicIncidentSummary has no actor-id property at all — this test documents that
        // constraint so a future field addition can't silently reintroduce it.
        typeof(PublicIncidentSummary).GetProperties().Should().NotContain(p => p.Name.Contains("User"));
    }

    [Fact]
    public async Task Uptime_history_covers_a_rolling_30_day_window_ending_today()
    {
        var repository = new InMemoryIncidentRepository();

        var handler = new GetPublicStatusQueryHandler(repository);

        var result = await handler.Handle(new GetPublicStatusQuery(), CancellationToken.None);

        result.UptimeHistory.Should().HaveCount(30);
        result.UptimeHistory.Should().BeInAscendingOrder(d => d.Date);
        result.UptimeHistory.Should().OnlyContain(d => !d.HadMajorIncident);
    }

    [Fact]
    public async Task A_resolved_major_incident_marks_only_the_days_it_was_actually_open()
    {
        var repository = new InMemoryIncidentRepository();
        var declaredAt = Now.AddDays(-3);
        var incident = Incident.Declare("Checkout outage", Severity.Sev1_Critical, "user-1", "fp-1", declaredAt);
        incident.Resolve("user-1", "Rolled back the bad deploy.", declaredAt.AddHours(2));
        repository.Add(incident);

        var handler = new GetPublicStatusQueryHandler(repository);

        var result = await handler.Handle(new GetPublicStatusQuery(), CancellationToken.None);

        var affectedDate = DateOnly.FromDateTime(declaredAt.UtcDateTime);
        result.UptimeHistory.Single(d => d.Date == affectedDate).HadMajorIncident.Should().BeTrue();
        result.UptimeHistory.Where(d => d.Date != affectedDate).Should().OnlyContain(d => !d.HadMajorIncident);
    }
}
