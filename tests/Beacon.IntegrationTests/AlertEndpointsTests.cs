using System.Net;
using System.Net.Http.Json;
using Beacon.Infrastructure.Alerts;
using FluentAssertions;

namespace Beacon.IntegrationTests;

/// <summary>
/// Covers the raw-line ingestion path added in Phase 13 (native/log-parser-shim). The parsing
/// logic itself already has thorough coverage on the native side
/// (native/log-parser-shim/test_log_parser.c: five cases plus a throughput benchmark, run
/// standalone against the C directly) — what actually needs checking here is the P/Invoke
/// boundary itself (does the struct marshal correctly across managed/unmanaged?) and the full
/// HTTP round trip, neither of which the native-side tests can see.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AlertEndpointsTests(BeaconApiFactory factory)
{
    [Fact]
    public void LogParserShim_marshals_a_well_formed_line_correctly_across_the_pinvoke_boundary()
    {
        var result = LogParserShim.Parse("Sev1_Critical|prometheus|HighErrorRate|payments-api-9|Error rate exceeded 5% threshold");

        result.Should().NotBeNull();
        result!.Severity.Should().Be("Sev1_Critical");
        result.Source.Should().Be("prometheus");
        result.AlertName.Should().Be("HighErrorRate");
        result.Instance.Should().Be("payments-api-9");
        result.Message.Should().Be("Error rate exceeded 5% threshold");
    }

    [Fact]
    public void LogParserShim_returns_null_for_a_malformed_line_instead_of_throwing()
    {
        var result = LogParserShim.Parse("not-enough-fields");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Ingest_raw_declares_an_incident_the_same_way_the_structured_endpoint_does()
    {
        var anonymous = factory.CreateClient();
        var fingerprint = $"raw-ingest-{Guid.NewGuid():N}";

        var response = await anonymous.PostAsJsonAsync("/api/alerts/ingest-raw", new
        {
            rawLine = $"Sev2_High|datadog|LatencySpike|checkout-api-{fingerprint}|p99 latency exceeded 800ms"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IngestResultDto>();
        result!.Decision.Should().Be("New");
        result.IncidentId.Should().NotBeNull();
    }

    /// <summary>
    /// This machine's Docker daemon has been down for every phase of this project (see
    /// LEARNING_GUIDE.md's Phase 3 entry) — so every alert-ingestion test in this class is, in
    /// practice, already running against a genuinely unreachable Redis, not a mock of one. Before
    /// RedisRecentAlertsStore's fail-open fix, this exact test 500'd: abortConnect=false only
    /// keeps the initial connection attempt from throwing, not the individual commands issued
    /// against a still-disconnected multiplexer. This test exists to name that behavior
    /// explicitly rather than leave it as an implicit side effect of the environment being broken.
    /// </summary>
    [Fact]
    public async Task Alert_ingestion_survives_Redis_being_unreachable_instead_of_500ing()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/alerts/ingest-raw", new
        {
            rawLine = $"Sev3_Medium|prometheus|RedisDegradeCheck|instance-{Guid.NewGuid():N}|verifying dedup fails open"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ingest_raw_rejects_a_malformed_line_with_400_not_500()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/alerts/ingest-raw", new { rawLine = "garbage" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ingest_raw_rejects_an_unrecognized_severity_with_400()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/alerts/ingest-raw", new
        {
            rawLine = "SevWhat|source|alert|instance|message"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record IngestResultDto(string Decision, Guid? IncidentId);
}
