using Beacon.Application.Alerts;

namespace Beacon.Api.Alerts;

public static class AlertEndpoints
{
    public static RouteGroupBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alerts").WithTags("Alerts");

        group.MapPost("/ingest", async (
                IngestAlertRequest request,
                IngestAlertHandler handler,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                return Results.Ok(result);
            })
            .WithName("IngestAlert")
            .WithSummary("Ingest a monitoring alert — deduplicates within a 5-minute window, declares a new incident on first sight")
            .Produces<IngestAlertResult>();

        return group;
    }
}
