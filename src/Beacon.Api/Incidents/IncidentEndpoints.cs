using Beacon.Application.Incidents;
using Beacon.Domain.Incidents;

namespace Beacon.Api.Incidents;

public static class IncidentEndpoints
{
    public static RouteGroupBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents").WithTags("Incidents");

        group.MapPost("/", async (
                DeclareIncidentRequest request,
                DeclareIncidentHandler handler,
                CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(request, ct);
                return Results.Created($"/api/incidents/{id}", new { id });
            })
            .WithName("DeclareIncident")
            .WithSummary("Declare a new incident")
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (
                Guid id,
                GetIncidentHandler handler,
                CancellationToken ct) =>
            {
                var response = await handler.HandleAsync(new IncidentId(id), ct);
                return Results.Ok(response);
            })
            .WithName("GetIncident")
            .Produces<IncidentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/acknowledge", async (
                Guid id,
                AcknowledgeIncidentBody body,
                AcknowledgeIncidentHandler handler,
                CancellationToken ct) =>
            {
                await handler.HandleAsync(new AcknowledgeIncidentRequest(new IncidentId(id), body.UserId), ct);
                return Results.NoContent();
            })
            .WithName("AcknowledgeIncident")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/resolve", async (
                Guid id,
                ResolveIncidentBody body,
                ResolveIncidentHandler handler,
                CancellationToken ct) =>
            {
                await handler.HandleAsync(new ResolveIncidentRequest(new IncidentId(id), body.UserId, body.Summary), ct);
                return Results.NoContent();
            })
            .WithName("ResolveIncident")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private sealed record AcknowledgeIncidentBody(string UserId);

    private sealed record ResolveIncidentBody(string UserId, string Summary);
}
