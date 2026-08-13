using Beacon.Application.Alerts;
using MediatR;

namespace Beacon.Api.Alerts;

public static class AlertEndpoints
{
    public static RouteGroupBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alerts").WithTags("Alerts");

        // Deliberately not .RequireAuthorization() — the caller here is a monitoring tool
        // (Prometheus Alertmanager, in the real analog), not an interactive user with a login
        // flow. Machine-to-machine ingestion needs its own auth mechanism (an API key or mTLS
        // client cert bound to a service identity, not a JWT issued to a human who logged in) —
        // a real, different problem from Phase 7's user auth, deliberately left open rather than
        // bolted on incorrectly just to make every endpoint uniformly "protected."
        group.MapPost("/ingest", async (
                IngestAlertCommand command,
                ISender sender,
                CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return Results.Ok(result);
            })
            .WithName("IngestAlert")
            .WithSummary("Ingest a monitoring alert — deduplicates within a 5-minute window, declares a new incident on first sight")
            .Produces<IngestAlertResult>();

        return group;
    }
}
