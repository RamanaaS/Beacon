using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace Beacon.IntegrationTests;

/// <summary>
/// This machine's Docker daemon has been down for every phase of this project (see
/// LEARNING_GUIDE.md's Phase 3/13/15 entries), so — like every other Redis-touching test in this
/// suite — these run against a genuinely unreachable Redis, not a mock of one. That's exactly what
/// makes them a real test of the design: /health has to stay 200 despite it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class HealthEndpointsTests(BeaconApiFactory factory)
{
    [Fact]
    public async Task Health_is_ready_even_though_Redis_is_unreachable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        // The whole point of Phase 18's "ready" vs. "dependencies" tag split: SQL (a hard
        // dependency this API cannot function without) gates readiness, Redis (Phase 13 built
        // RedisRecentAlertsStore specifically to degrade gracefully without it) does not.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Alive_is_always_ok_regardless_of_any_downstream_dependency()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/alive");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HealthResponseDto>();
        body!.Checks.Should().ContainSingle(c => c.Name == "self" && c.Status == "Healthy");
    }

    [Fact]
    public async Task Health_detail_shows_every_check_including_the_degraded_one_health_itself_hides()
    {
        var client = factory.CreateClient();

        // /health/detail's own aggregate status can legitimately be 503 here (it includes every
        // check, Redis included) — the response *body*, not the status code, is what this test
        // actually cares about: that Redis's real state is visible somewhere, even though /health
        // deliberately excludes it from gating traffic.
        var response = await client.GetAsync("/health/detail");
        var body = await response.Content.ReadFromJsonAsync<HealthResponseDto>();

        body!.Checks.Should().Contain(c => c.Name == "sql" && c.Status == "Healthy");
        body.Checks.Should().Contain(c => c.Name == "redis" && c.Status == "Unhealthy");
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id_header()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.Headers.Should().ContainKey("X-Correlation-Id");
        response.Headers.GetValues("X-Correlation-Id").Single().Should().NotBeNullOrWhiteSpace();
    }

    // System.Net.Http.Json's ReadFromJsonAsync does NOT use ASP.NET Core's case-insensitive web
    // JSON defaults (the exact gotcha Phase 8 hit and documented) — HealthCheckJsonWriter emits
    // plain lowercase/camelCase keys ("status", "checks", "name"), so these need explicit
    // [JsonPropertyName] rather than relying on PascalCase-to-camelCase matching that won't happen.
    private sealed record HealthResponseDto(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("checks")] List<HealthCheckDto> Checks);

    private sealed record HealthCheckDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("status")] string Status);
}
