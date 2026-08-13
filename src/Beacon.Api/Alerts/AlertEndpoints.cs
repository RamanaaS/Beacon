using Beacon.Application.Alerts;
using MediatR;

namespace Beacon.Api.Alerts;

public static class AlertEndpoints
{
    public static RouteGroupBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alerts").WithTags("Alerts");

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
