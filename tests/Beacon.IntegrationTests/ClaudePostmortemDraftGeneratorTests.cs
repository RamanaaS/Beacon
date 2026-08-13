using System.Net;
using System.Text;
using System.Text.Json;
using Beacon.Application.Common;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using Beacon.Infrastructure.Incidents;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Beacon.IntegrationTests;

/// <summary>
/// Doesn't use BeaconApiFactory or a real network call — ClaudePostmortemDraftGenerator is
/// constructed directly with a stubbed HttpMessageHandler that returns a realistic Anthropic
/// Messages API response. This verifies the actual HTTP contract (URL, headers, request body
/// shape, response parsing) without needing a real API key or making a real, billable call —
/// while still being a genuine test of the wire format, not a mock that just returns whatever
/// the test tells it to.
/// </summary>
public sealed class ClaudePostmortemDraftGeneratorTests
{
    [Fact]
    public async Task Sends_correct_request_shape_and_parses_a_realistic_Anthropic_response()
    {
        const string responseJson = """
            {
              "id": "msg_test123",
              "type": "message",
              "role": "assistant",
              "model": "claude-sonnet-5",
              "content": [
                { "type": "text", "text": "## Summary\nThe payments API returned 500s due to a bad deploy." }
              ],
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 250, "output_tokens": 80 }
            }
            """;

        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var settings = Options.Create(new ClaudeSettings { ApiKey = "test-api-key", Model = "claude-sonnet-5", MaxTokens = 1024 });
        var generator = new ClaudePostmortemDraftGenerator(httpClient, settings);

        var incident = Incident.Declare("Payments API returning 500s", Severity.Sev1_Critical, "user-1", "fp-1", DateTimeOffset.UtcNow);
        incident.Resolve("user-7", "Rolled back the bad deploy.", DateTimeOffset.UtcNow.AddMinutes(10));

        var draft = await generator.GenerateDraftAsync(incident);

        draft.Should().Be("## Summary\nThe payments API returned 500s due to a bad deploy.");

        handler.CapturedRequest.Should().NotBeNull();
        handler.CapturedRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/messages");
        handler.CapturedRequest.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("test-api-key");
        handler.CapturedRequest.Headers.GetValues("anthropic-version").Should().ContainSingle().Which.Should().Be("2023-06-01");

        using var bodyDoc = JsonDocument.Parse(handler.CapturedRequestBody!);
        bodyDoc.RootElement.GetProperty("model").GetString().Should().Be("claude-sonnet-5");
        bodyDoc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(1024);
        var promptSent = bodyDoc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        promptSent.Should().Contain("Payments API returning 500s");
    }

    [Fact]
    public async Task Missing_api_key_throws_without_making_any_request()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var settings = Options.Create(new ClaudeSettings { ApiKey = "" });
        var generator = new ClaudePostmortemDraftGenerator(httpClient, settings);

        var incident = Incident.Declare("Test", Severity.Sev2_High, "user-1", "fp-1", DateTimeOffset.UtcNow);
        incident.Resolve("user-1", "fixed", DateTimeOffset.UtcNow);

        var act = () => generator.GenerateDraftAsync(incident);

        await act.Should().ThrowAsync<ExternalServiceNotConfiguredException>();
        handler.CapturedRequest.Should().BeNull();
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public string? CapturedRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            if (request.Content is not null)
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
