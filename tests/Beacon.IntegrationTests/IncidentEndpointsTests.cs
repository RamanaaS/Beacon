using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Beacon.IntegrationTests;

/// <summary>
/// Deliberately uses its own plain request/response shapes instead of referencing
/// Beacon.Application's DeclareIncidentRequest/IncidentResponse types directly. Asserting
/// against the actual wire format (what a real HTTP client would send/receive) is what an
/// integration test is for — reusing the server's exact C# types would hide a real serialization
/// break behind a compile-time contract that isn't what's actually crossing the network.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IncidentEndpointsTests(BeaconApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Declare_then_get_returns_the_incident_with_one_event()
    {
        var id = await DeclareAsync("Payments API returning 500s", "fp-abc123");

        var incident = await GetAsync(id);

        incident.Title.Should().Be("Payments API returning 500s");
        incident.Severity.Should().Be("Sev1_Critical");
        incident.Status.Should().Be("Declared");
        incident.EventCount.Should().Be(1);
    }

    [Fact]
    public async Task Full_lifecycle_declare_acknowledge_resolve_progresses_status_and_event_count()
    {
        var id = await DeclareAsync("Full lifecycle test incident", "fp-lifecycle");

        (await _client.PostAsJsonAsync($"/api/incidents/{id}/acknowledge", new { userId = "user-7" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterAck = await GetAsync(id);
        afterAck.Status.Should().Be("Acknowledged");
        afterAck.EventCount.Should().Be(2);

        (await _client.PostAsJsonAsync($"/api/incidents/{id}/resolve",
                new { userId = "user-7", summary = "Rolled back the bad deploy." }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterResolve = await GetAsync(id);
        afterResolve.Status.Should().Be("Resolved");
        afterResolve.EventCount.Should().Be(3);
    }

    [Fact]
    public async Task Get_with_unknown_id_returns_404()
    {
        var response = await _client.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Declare_with_empty_title_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/incidents", new
        {
            title = "",
            severity = "Sev2_High",
            declaredByUserId = "user-1",
            alertFingerprint = "fp-empty-title"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Escalate_sets_IsEscalated_and_appends_an_event()
    {
        var id = await DeclareAsync("Needs escalation test incident", "fp-escalate");

        (await _client.PostAsJsonAsync($"/api/incidents/{id}/escalate",
                new { escalatedByUserId = "user-9", escalatedTo = "secondary-oncall" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var incident = await GetAsync(id);
        incident.IsEscalated.Should().BeTrue();
        incident.EventCount.Should().Be(2); // Declared + Escalated
    }

    [Fact]
    public async Task Acting_on_a_resolved_incident_returns_409()
    {
        var id = await DeclareAsync("Already resolved test incident", "fp-already-resolved");

        (await _client.PostAsJsonAsync($"/api/incidents/{id}/resolve",
            new { userId = "user-1", summary = "closed" })).EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync($"/api/incidents/{id}/acknowledge", new { userId = "user-1" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<Guid> DeclareAsync(string title, string fingerprint)
    {
        var response = await _client.PostAsJsonAsync("/api/incidents", new
        {
            title,
            severity = "Sev1_Critical",
            declaredByUserId = "user-42",
            alertFingerprint = fingerprint
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedIncidentDto>();
        return created!.Id;
    }

    private async Task<IncidentDto> GetAsync(Guid id)
    {
        var response = await _client.GetAsync($"/api/incidents/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IncidentDto>())!;
    }

    private sealed record CreatedIncidentDto(Guid Id);

    private sealed record IncidentDto(
        Guid Id,
        string Title,
        string Severity,
        string Status,
        List<string> Responders,
        bool IsEscalated,
        int EventCount);
}
