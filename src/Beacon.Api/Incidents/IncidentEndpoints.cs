using Beacon.Application.Incidents;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Api.Incidents;

public static class IncidentEndpoints
{
    public static RouteGroupBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents").WithTags("Incidents");

        group.MapPost("/", async (
                DeclareIncidentCommand command,
                ISender sender,
                CancellationToken ct) =>
            {
                var id = await sender.Send(command, ct);
                return Results.Created($"/api/incidents/{id}", new { id });
            })
            .WithName("DeclareIncident")
            .WithSummary("Declare a new incident")
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                var response = await sender.Send(new GetIncidentQuery(new IncidentId(id)), ct);
                return Results.Ok(response);
            })
            .WithName("GetIncident")
            .Produces<IncidentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/acknowledge", async (
                Guid id,
                AcknowledgeIncidentBody body,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(new AcknowledgeIncidentCommand(new IncidentId(id), body.UserId), ct);
                return Results.NoContent();
            })
            .WithName("AcknowledgeIncident")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/resolve", async (
                Guid id,
                ResolveIncidentBody body,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(new ResolveIncidentCommand(new IncidentId(id), body.UserId, body.Summary), ct);
                return Results.NoContent();
            })
            .WithName("ResolveIncident")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/escalate", async (
                Guid id,
                EscalateIncidentBody body,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(new EscalateIncidentCommand(new IncidentId(id), body.EscalatedByUserId, body.EscalatedTo), ct);
                return Results.NoContent();
            })
            .WithName("EscalateIncident")
            .WithSummary("Manually escalate an incident (the same command EscalationCheckerService issues automatically)")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/report.xlsx", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                var bytes = await sender.Send(new GenerateIncidentExcelReportQuery(new IncidentId(id)), ct);
                return Results.File(
                    bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"incident-{id}.xlsx");
            })
            .WithName("GetIncidentExcelReport")
            .WithSummary("Download the incident's timeline as an Excel workbook")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/report.pdf", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                var bytes = await sender.Send(new GenerateIncidentPdfReportQuery(new IncidentId(id)), ct);
                return Results.File(bytes, "application/pdf", $"incident-{id}.pdf");
            })
            .WithName("GetIncidentPdfReport")
            .WithSummary("Download the incident as a PDF postmortem report")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private sealed record AcknowledgeIncidentBody(string UserId);

    private sealed record ResolveIncidentBody(string UserId, string Summary);

    private sealed record EscalateIncidentBody(string EscalatedByUserId, string EscalatedTo);
}
