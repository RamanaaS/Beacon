using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Beacon.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Takes the calling assembly (usually Beacon.Api) as an extra scan target alongside
    /// Beacon.Application's own — that's what lets a notification handler like
    /// IncidentDeclaredNotificationHandler live in Beacon.Api (where SignalR actually is) and
    /// still get discovered by MediatR, without Application ever referencing Api or SignalR.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, params Assembly[] additionalHandlerAssemblies)
    {
        Assembly[] assemblies = [typeof(DependencyInjection).Assembly, .. additionalHandlerAssemblies];

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(assemblies));

        return services;
    }
}
