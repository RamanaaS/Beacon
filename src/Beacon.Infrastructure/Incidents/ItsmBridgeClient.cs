using System.Net.Http.Json;
using Beacon.Application.Incidents;

namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// HTTP client for src/itsm-bridge (Java Spring Boot) — a real, separately-deployed process, not
/// another in-process abstraction. Resilience (retry, circuit breaker, timeout) comes from
/// AddStandardResilienceHandler() at registration (DependencyInjection.cs), the same Polly
/// approach Phase 8 used for the Claude API client — ARCHITECTURE.md names both calls explicitly
/// as needing it (Table B: "Polly - Around the ITSM-bridge call and the LLM API call").
///
/// This class itself does NOT catch failures — ItsmTicketSyncHandler (the caller) does, and that
/// split is deliberate: this class's job is "make the HTTP call correctly," the caller's job is
/// "decide what an itsm-bridge outage should mean for the incident flow that triggered it." Baking
/// swallow-and-log into the client itself would hide that decision from whoever reads the call site.
/// </summary>
public sealed class ItsmBridgeClient(HttpClient httpClient) : IItsmBridgeClient
{
    public async Task CreateTicketAsync(string incidentId, string title, string severity, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/tickets", new { incidentId, title, severity }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task CloseTicketAsync(string incidentId, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/tickets/by-incident/{incidentId}/status", new { status = "CLOSED" }, ct);
        response.EnsureSuccessStatusCode();
    }
}
