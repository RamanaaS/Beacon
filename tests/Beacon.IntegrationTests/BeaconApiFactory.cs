using Beacon.Infrastructure.Incidents;
using Microsoft.AspNetCore.Hosting;
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
/// </summary>
public sealed class BeaconApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=BeaconDb_IntegrationTests;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

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
    }

    // xUnit's IAsyncLifetime — runs once before any test in a class using this fixture.
    public Task InitializeAsync() => ResetDatabaseAsync();

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}
