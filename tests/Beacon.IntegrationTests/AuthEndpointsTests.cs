using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Beacon.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthEndpointsTests(BeaconApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Register_then_login_returns_a_usable_token_with_the_chosen_role()
    {
        var email = $"responder-{Guid.NewGuid():N}@beacon.test";

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Correct-Horse-Battery-9",
            displayName = "Test Responder",
            role = "Responder"
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Correct-Horse-Battery-9"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        login!.Token.Should().NotBeNullOrWhiteSpace();
        login.Roles.Should().Contain("Responder");
        login.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var email = $"wrongpw-{Guid.NewGuid():N}@beacon.test";
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Correct-Horse-Battery-9",
            displayName = "Test User",
            role = "Viewer"
        });

        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "totally-wrong" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_with_an_unknown_role_returns_a_validation_problem()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"badrole-{Guid.NewGuid():N}@beacon.test",
            password = "Correct-Horse-Battery-9",
            displayName = "Test User",
            role = "SuperAdmin"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_token_issued_by_login_actually_authorizes_a_protected_endpoint()
    {
        var email = $"e2e-{Guid.NewGuid():N}@beacon.test";
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Correct-Horse-Battery-9",
            displayName = "End To End",
            role = "Responder"
        });
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Correct-Horse-Battery-9" });
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();

        var authedClient = factory.CreateAuthenticatedClient(login!.Token);
        var declareResponse = await authedClient.PostAsJsonAsync("/api/incidents", new
        {
            title = "Declared using a token minted by /api/auth/login itself",
            severity = "Sev3_Medium",
            alertFingerprint = $"fp-e2e-auth-{Guid.NewGuid():N}"
        });

        declareResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private sealed record LoginResponseDto(string Token, DateTimeOffset ExpiresAt, List<string> Roles);
}
