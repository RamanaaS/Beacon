using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Beacon.Admin.Services;

public sealed record LoginResult(bool Success, string? Token, IReadOnlyList<string> Roles, string? Error);

public sealed record AdminUser(string Id, string Email, string DisplayName, List<string> Roles);

public sealed record IncidentSummary(string Id, string Title, string Severity, string Status, bool IsEscalated, DateTimeOffset DeclaredAt);

/// <summary>
/// Attaches the bearer token itself (via <paramref name="authState"/>) rather than through an
/// IHttpClientFactory message handler — see the comment in Program.cs for why a handler-based
/// approach silently breaks under Blazor Server's per-circuit scoping.
/// </summary>
public sealed class BeaconApiClient(HttpClient http, AdminAuthState authState)
{
    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        var response = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
        if (!response.IsSuccessStatusCode)
            return new LoginResult(false, null, [], "Invalid email or password.");

        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return new LoginResult(true, body!.Token, body.Roles, null);
    }

    public async Task<List<AdminUser>> ListUsersAsync()
    {
        ApplyAuthHeader();
        return await http.GetFromJsonAsync<List<AdminUser>>("/api/admin/users") ?? [];
    }

    public async Task<bool> UpdateUserRoleAsync(string userId, string role)
    {
        ApplyAuthHeader();
        var response = await http.PutAsJsonAsync($"/api/admin/users/{userId}/role", new { role });
        return response.IsSuccessStatusCode;
    }

    public async Task<List<IncidentSummary>> ListIncidentsAsync()
    {
        ApplyAuthHeader();
        return await http.GetFromJsonAsync<List<IncidentSummary>>("/api/incidents") ?? [];
    }

    // GetFromJsonAsync/PutAsJsonAsync have no per-call header-setting overload, so the token is
    // applied to the client's default headers immediately before each authenticated call instead.
    private void ApplyAuthHeader()
    {
        if (authState.Token is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authState.Token);
    }

    private sealed record LoginResponseDto(string Token, DateTimeOffset ExpiresAt, List<string> Roles);
}
