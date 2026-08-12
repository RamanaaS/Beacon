using Beacon.Application.Alerts;
using Beacon.Application.Common;
using Beacon.Application.Incidents;
using Beacon.Infrastructure.Alerts;
using Beacon.Infrastructure.Common;
using Beacon.Infrastructure.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Beacon.Infrastructure;

/// <summary>
/// Keeps Beacon.Api from needing a direct reference to EF Core, the SQL Server provider, or
/// StackExchange.Redis — it depends only on this method's signature, which lives in
/// Infrastructure alongside the implementations it wires up.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BeaconDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("BeaconDb")));

        services.AddScoped<IIncidentRepository, IncidentRepository>();
        services.AddSingleton<IClock, SystemClock>();

        // ConnectionMultiplexer.Connect normally throws immediately if Redis isn't reachable —
        // abortConnect=false (set on the connection string) lets the app start regardless and
        // retries in the background, so a Redis outage degrades only the alert-ingestion
        // endpoint that actually needs it, not the whole API.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis")!));
        services.AddSingleton<IRecentAlertsStore, RedisRecentAlertsStore>();

        return services;
    }
}
