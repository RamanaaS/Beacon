using Beacon.Infrastructure.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Beacon.Infrastructure.Postgres;

/// <summary>
/// Design-time only: lets `dotnet ef migrations add` generate PostgreSQL migrations into this
/// assembly without a running database or the API host. The connection string is never opened
/// when adding a migration; it only has to parse. Run from the repo root:
///   dotnet ef migrations add &lt;Name&gt; --project src/Beacon.Infrastructure.Postgres --startup-project src/Beacon.Infrastructure.Postgres --output-dir Migrations
/// </summary>
public sealed class PostgresBeaconDbContextFactory : IDesignTimeDbContextFactory<BeaconDbContext>
{
    public BeaconDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BeaconDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=beacon;Username=beacon;Password=design-time-only",
            npgsql => npgsql.MigrationsAssembly(DatabaseProvider.PostgresMigrationsAssembly));

        return new BeaconDbContext(optionsBuilder.Options);
    }
}
