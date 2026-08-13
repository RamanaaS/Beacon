using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Beacon.Application.Common;
using Beacon.Application.Incidents;
using Beacon.Domain.Incidents;
using Microsoft.Extensions.Options;

namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// A direct, hand-rolled call to the Anthropic Messages API rather than pulling in an official
/// SDK — deliberately, for a portfolio project: it keeps the actual HTTP contract (headers,
/// request/response shape, resilience policy) visible and testable with a stubbed
/// HttpMessageHandler, instead of hidden behind SDK internals. A real production system consuming
/// several Anthropic endpoints would likely prefer the official SDK for maintenance reasons —
/// noted, not chosen here, because "one endpoint, fully understood" served this project's actual
/// purpose better than "several endpoints, less visible."
/// </summary>
public sealed class ClaudePostmortemDraftGenerator(HttpClient httpClient, IOptions<ClaudeSettings> options)
    : IPostmortemDraftGenerator
{
    public async Task<string> GenerateDraftAsync(Incident incident, CancellationToken ct = default)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new ExternalServiceNotConfiguredException(
                "Claude API",
                "Set Claude:ApiKey via `dotnet user-secrets set Claude:ApiKey <key>` locally, " +
                "or Key Vault/environment in a real deployment — never appsettings.json.");

        var requestBody = new ClaudeMessageRequest(
            settings.Model,
            settings.MaxTokens,
            [new ClaudeMessage("user", PostmortemPromptBuilder.Build(incident))]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("x-api-key", settings.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ClaudeMessageResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty response body from Claude API.");

        var text = payload.Content.FirstOrDefault(block => block.Type == "text")?.Text;
        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidOperationException("Claude API response contained no text content block.")
            : text;
    }

    // Every property has an explicit [JsonPropertyName] rather than relying on
    // PropertyNameCaseInsensitive defaults — System.Net.Http.Json's ReadFromJsonAsync does NOT
    // use ASP.NET Core's web JSON defaults (which are case-insensitive); without these attributes,
    // "content"/"type"/"text" in Anthropic's real lowercase JSON silently wouldn't bind to
    // PascalCase C# properties, and this would fail with an unhelpful "no text content" error
    // even against a perfectly valid response.
    private sealed record ClaudeMessageRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<ClaudeMessage> Messages);

    private sealed record ClaudeMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ClaudeMessageResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<ClaudeContentBlock> Content);

    private sealed record ClaudeContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);
}
