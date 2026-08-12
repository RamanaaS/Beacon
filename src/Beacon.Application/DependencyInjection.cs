using Beacon.Application.Incidents;
using Microsoft.Extensions.DependencyInjection;

namespace Beacon.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<DeclareIncidentHandler>();
        services.AddScoped<AcknowledgeIncidentHandler>();
        services.AddScoped<ResolveIncidentHandler>();
        services.AddScoped<GetIncidentHandler>();

        return services;
    }
}
