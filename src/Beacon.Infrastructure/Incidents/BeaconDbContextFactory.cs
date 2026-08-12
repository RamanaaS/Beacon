using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// Only used by the `dotnet ef` CLI at design time (generating/applying migrations) — there's
/// no Beacon.Api host project wiring up configuration yet for it to piggyback on. Once Beacon.Api
/// exists with its own appsettings-based connection string, this stays only for the case where
/// you want to run `dotnet ef` against Beacon.Infrastructure directly without starting the API.
/// </summary>
public sealed class BeaconDbContextFactory : IDesignTimeDbContextFactory<BeaconDbContext>
{
    public BeaconDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BeaconDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\MSSQLLocalDB;Database=BeaconDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");

        return new BeaconDbContext(optionsBuilder.Options);
    }
}
