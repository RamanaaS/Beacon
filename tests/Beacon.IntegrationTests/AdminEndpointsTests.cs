using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Beacon.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AdminEndpointsTests(BeaconApiFactory factory)
{
    [Fact]
    public async Task Responder_role_cannot_list_users()
    {
        var responderClient = factory.CreateAuthenticatedClient(factory.ResponderToken);

        var response = await responderClient.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_role_cannot_list_users()
    {
        var viewerClient = factory.CreateAuthenticatedClient(factory.ViewerToken);

        var response = await viewerClient.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_role_can_list_users_and_change_a_role()
    {
        var adminEmail = $"admin-{Guid.NewGuid():N}@beacon.test";
        var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync("/api/auth/register", new
        {
            email = adminEmail, password = "Correct-Horse-Battery-9", displayName = "Test Admin", role = "Admin",
        });
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = adminEmail, password = "Correct-Horse-Battery-9" });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.Token;
        var adminClient = factory.CreateAuthenticatedClient(token);

        var listResponse = await adminClient.GetAsync("/api/admin/users");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = await listResponse.Content.ReadFromJsonAsync<List<AdminUserDto>>();
        var self = users!.Single(u => u.Email == adminEmail);
        self.Roles.Should().Contain("Admin");

        var updateResponse = await adminClient.PutAsJsonAsync($"/api/admin/users/{self.Id}/role", new { role = "Viewer" });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterUpdate = await adminClient.GetAsync("/api/admin/users");
        var updatedSelf = (await afterUpdate.Content.ReadFromJsonAsync<List<AdminUserDto>>())!.Single(u => u.Id == self.Id);
        updatedSelf.Roles.Should().BeEquivalentTo(["Viewer"]);
    }

    [Fact]
    public async Task Updating_role_to_an_unknown_value_returns_400()
    {
        var adminEmail = $"admin-{Guid.NewGuid():N}@beacon.test";
        var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync("/api/auth/register", new
        {
            email = adminEmail, password = "Correct-Horse-Battery-9", displayName = "Test Admin", role = "Admin",
        });
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = adminEmail, password = "Correct-Horse-Battery-9" });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.Token;
        var adminClient = factory.CreateAuthenticatedClient(token);

        var users = await (await adminClient.GetAsync("/api/admin/users")).Content.ReadFromJsonAsync<List<AdminUserDto>>();
        var target = users!.Single(u => u.Email == adminEmail);

        var response = await adminClient.PutAsJsonAsync($"/api/admin/users/{target.Id}/role", new { role = "SuperUser" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record LoginResponseDto(string Token, DateTimeOffset ExpiresAt, List<string> Roles);

    private sealed record AdminUserDto(string Id, string Email, string DisplayName, List<string> Roles);
}
