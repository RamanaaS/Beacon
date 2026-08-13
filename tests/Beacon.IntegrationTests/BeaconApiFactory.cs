using System.Net.Http.Headers;
using Beacon.Infrastructure.Auth;
using Beacon.Infrastructure.Incidents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beacon.IntegrationTests;

/// <summary>
/// Points the whole API at a dedicated LocalDB database (never the dev one from `dotnet run`)
/// and gives it a clean slate for every test class run. This is what Phase 15 (Testcontainers)
/// will eventually replace with a disposable containerized SQL Server — LocalDB is the pragmatic
/// stand-in for now since it's already on this machine and is genuinely SQL Server underneath.
///
/// Also seeds one Responder and one Viewer test user (with real JWTs from the real token
/// generator, not a hand-rolled test-only token) once per collection run — every test that needs
/// an authenticated client uses CreateAuthenticatedClient(ResponderToken/ViewerToken) instead of
/// the bare CreateClient() every endpoint now rejects.
/// </summary>
public sealed class BeaconApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=BeaconDb_IntegrationTests;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public string ResponderToken { get; private set; } = string.Empty;
    public string ViewerToken { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BeaconDb"] = ConnectionString
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> CreateUserAndGetTokenAsync(string role)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@beacon.test";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = $"Test {role}" };

        var result = await userManager.CreateAsync(user, "Test-Password-123!");
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, role);

        var (token, _) = tokenGenerator.GenerateToken(user.Id, email, [role]);
        return token;
    }

    // xUnit's IAsyncLifetime — runs once before any test in the collection using this fixture.
    public async Task InitializeAsync()
    {
        await ResetDatabaseAsync();
        ResponderToken = await CreateUserAndGetTokenAsync(Roles.Responder);
        ViewerToken = await CreateUserAndGetTokenAsync(Roles.Viewer);
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}
