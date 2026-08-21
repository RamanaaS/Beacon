using Beacon.Application.Incidents;
using MediatR;

namespace Beacon.Api.Status;

/// <summary>
/// Deliberately its own file/group, not folded into IncidentEndpoints — that group carries a
/// blanket .RequireAuthorization(), and this is the one route in the system a customer with no
/// account is meant to reach. Same "public/no-auth is a deliberate, isolated decision, not an
/// accident" reasoning as Phase 3's alert-ingestion endpoint and Phase 4's telemetry WebSocket.
/// </summary>
public static class PublicStatusEndpoints
{
    public static RouteGroupBuilder MapPublicStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/status").WithTags("Status");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            {
                var response = await sender.Send(new GetPublicStatusQuery(), ct);
                return Results.Ok(response);
            })
            .WithName("GetPublicStatus")
            .WithSummary("Public, unauthenticated system status — backs Beacon.StatusPage")
            .CacheOutput(policy => policy.Expire(TimeSpan.FromSeconds(15)))
            .Produces<PublicStatusResponse>();

        return group;
    }
}
