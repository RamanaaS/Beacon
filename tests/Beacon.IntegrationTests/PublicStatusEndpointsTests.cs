using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Beacon.IntegrationTests;

/// <summary>
/// Every other endpoint test class authenticates first — this one deliberately never does,
/// because the whole point of /api/status is that it works for a client with no account at all.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PublicStatusEndpointsTests(BeaconApiFactory factory)
{
    [Fact]
    public async Task Anonymous_client_can_read_status_with_no_token()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_newly_declared_incident_appears_in_the_public_feed_without_exposing_the_actor()
    {
        var responderClient = factory.CreateAuthenticatedClient(factory.ResponderToken);
        var title = $"Public feed check {Guid.NewGuid():N}";
        var declare = await responderClient.PostAsJsonAsync("/api/incidents", new
        {
            title,
            severity = "Sev1_Critical",
            alertFingerprint = $"fp-public-{Guid.NewGuid():N}"
        });
        declare.EnsureSuccessStatusCode();

        var anonymous = factory.CreateClient();
        var status = await (await anonymous.GetAsync("/api/status")).Content.ReadFromJsonAsync<PublicStatusDto>();

        status!.OverallStatus.Should().Be("MajorOutage");
        var match = status.ActiveIncidents.Should().ContainSingle(i => i.Title == title).Subject;
        match.Severity.Should().Be("Sev1_Critical");

        // The raw JSON, not just the deserialized DTO, has to genuinely lack the field — a typed
        // DTO with an extra property the server never actually sends would still pass otherwise.
        var raw = await (await anonymous.GetAsync("/api/status")).Content.ReadAsStringAsync();
        raw.Should().NotContain("UserId").And.NotContain("userId").And.NotContain("declaredBy", "case-insensitive enough for this check");
    }

    [Fact]
    public async Task Response_includes_a_30_day_uptime_history_and_the_fixed_component_list()
    {
        var anonymous = factory.CreateClient();

        var status = await (await anonymous.GetAsync("/api/status")).Content.ReadFromJsonAsync<PublicStatusDto>();

        status!.UptimeHistory.Should().HaveCount(30);
        status.Components.Should().Contain(c => c.Name == "payments-api");
    }

    private sealed record PublicStatusDto(
        string OverallStatus,
        List<PublicComponentDto> Components,
        List<PublicIncidentDto> ActiveIncidents,
        List<PublicUptimeDayDto> UptimeHistory);

    private sealed record PublicComponentDto(string Name, string Status);

    private sealed record PublicIncidentDto(string Title, string Severity, string Status, DateTimeOffset DeclaredAt);

    private sealed record PublicUptimeDayDto(DateOnly Date, bool HadMajorIncident);
}
