using Microsoft.Extensions.DependencyInjection;

namespace Beacon.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // MediatR's assembly scan registers every IRequestHandler<> in this assembly — the
        // individual AddScoped<XxxCommandHandler>() calls this replaced were about to become
        // five, then six, lines of the exact same boilerplate for every new command.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
