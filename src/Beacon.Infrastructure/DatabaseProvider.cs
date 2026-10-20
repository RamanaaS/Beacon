using Microsoft.Extensions.Configuration;

namespace Beacon.Infrastructure;

/// <summary>
/// Reads "Database:Provider" — "SqlServer" (the default when unset) or "Postgres". Kept in one
/// place so DependencyInjection.cs and Beacon.Api's health-check registration can't disagree
/// about which database the app is actually talking to.
/// </summary>
public static class DatabaseProvider
{
    public const string PostgresMigrationsAssembly = "Beacon.Infrastructure.Postgres";

    public static bool IsPostgres(IConfiguration configuration) =>
        string.Equals(configuration["Database:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase);
}
