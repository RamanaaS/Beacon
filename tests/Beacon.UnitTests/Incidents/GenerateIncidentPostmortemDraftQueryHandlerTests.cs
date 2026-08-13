using Beacon.Application.Incidents;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using Beacon.UnitTests.Alerts;
using FluentAssertions;

namespace Beacon.UnitTests.Incidents;

public class GenerateIncidentPostmortemDraftQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resolved_incident_returns_the_generated_draft()
    {
        var repository = new InMemoryIncidentRepository();
        var incident = Incident.Declare("DB down", Severity.Sev1_Critical, "user-1", "fp-1", Now);
        incident.Resolve("user-7", "Restarted the connection pool.", Now.AddMinutes(10));
        repository.Add(incident);

        var handler = new GenerateIncidentPostmortemDraftQueryHandler(
            repository, new FakePostmortemDraftGenerator("## Summary\nEverything is fine now."), new MutableClock(Now));

        var result = await handler.Handle(new GenerateIncidentPostmortemDraftQuery(incident.Id), CancellationToken.None);

        result.Draft.Should().Be("## Summary\nEverything is fine now.");
    }

    [Fact]
    public async Task Unresolved_incident_throws_instead_of_calling_the_generator()
    {
        var repository = new InMemoryIncidentRepository();
        var incident = Incident.Declare("Still ongoing", Severity.Sev2_High, "user-1", "fp-2", Now);
        repository.Add(incident);

        var generator = new FakePostmortemDraftGenerator("should never be produced");
        var handler = new GenerateIncidentPostmortemDraftQueryHandler(repository, generator, new MutableClock(Now));

        var act = () => handler.Handle(new GenerateIncidentPostmortemDraftQuery(incident.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        generator.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_incident_throws_not_found()
    {
        var repository = new InMemoryIncidentRepository();
        var handler = new GenerateIncidentPostmortemDraftQueryHandler(
            repository, new FakePostmortemDraftGenerator("n/a"), new MutableClock(Now));

        var act = () => handler.Handle(new GenerateIncidentPostmortemDraftQuery(new IncidentId(Guid.NewGuid())), CancellationToken.None);

        await act.Should().ThrowAsync<IncidentNotFoundException>();
    }

    private sealed class FakePostmortemDraftGenerator(string draft) : IPostmortemDraftGenerator
    {
        public bool WasCalled { get; private set; }

        public Task<string> GenerateDraftAsync(Incident incident, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(draft);
        }
    }
}
