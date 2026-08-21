using System.Net.Http.Json;

namespace Beacon.StatusPage.Services;

/// <summary>
/// Server-to-server, this MVC app's own backend calling Beacon.Api's public /api/status — not a
/// browser fetch, so no CORS concern, same pattern as Beacon.Admin's BeaconApiClient. Unlike
/// BeaconApiClient there's no bearer token anywhere in this client: /api/status is the one
/// endpoint in the system that's unauthenticated by design, and this is its one real consumer.
/// </summary>
public sealed class BeaconStatusClient(HttpClient http)
{
    public async Task<PublicStatusResponse> GetStatusAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<PublicStatusResponse>("/api/status", ct)
        ?? throw new InvalidOperationException("Beacon.Api returned an empty /api/status response.");
}
