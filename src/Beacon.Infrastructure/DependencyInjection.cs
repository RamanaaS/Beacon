using Beacon.Application.Common;
using Beacon.Application.Incidents;
using Beacon.Infrastructure.Common;
using Beacon.Infrastructure.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beacon.Infrastructure;

/// <summary>
/// Keeps Beacon.Api from needing a direct reference to EF Core or the SQL Server provider —
/// it depends only on this method's signature, which lives in Infrastructure alongside the
/// implementation it wires up.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BeaconDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("BeaconDb")));

        services.AddScoped<IIncidentRepository, IncidentRepository>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
