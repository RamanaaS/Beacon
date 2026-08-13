using System.Security.Claims;
using Beacon.Api.Auth;
using Beacon.Application.Incidents;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Api.Incidents;

public static class IncidentEndpoints
{
    public static RouteGroupBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents").WithTags("Incidents").RequireAuthorization();

        group.MapPost("/", async (
                DeclareIncidentRequest request,
                ClaimsPrincipal user,
                ISender sender,
                CancellationToken ct) =>
            {
                var command = new DeclareIncidentCommand(
                    request.Title, request.Severity, user.GetUserId(), request.AlertFingerprint);
                var id = await sender.Send(command, ct);
                return Results.Created($"/api/incidents/{id}", new { id });
            })
            .WithName("DeclareIncident")
            .WithSummary("Declare a new incident")
            .RequireAuthorization("IncidentResponder")
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
                ClaimsPrincipal user,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(new AcknowledgeIncidentCommand(new IncidentId(id), user.GetUserId()), ct);
                return Results.NoContent();
            })
            .WithName("AcknowledgeIncident")
            .RequireAuthorization("IncidentResponder")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/resolve", async (
                Guid id,
                ResolveIncidentBody body,
                ClaimsPrincipal user,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(new ResolveIncidentCommand(new IncidentId(id), user.GetUserId(), body.Summary), ct);
                return Results.NoContent();
            })
            .WithName("ResolveIncident")
            .RequireAuthorization("IncidentResponder")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/escalate", async (
                Guid id,
                EscalateIncidentBody body,
                ClaimsPrincipal user,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(new EscalateIncidentCommand(new IncidentId(id), user.GetUserId(), body.EscalatedTo), ct);
                return Results.NoContent();
            })
            .WithName("EscalateIncident")
            .WithSummary("Manually escalate an incident (the same command EscalationCheckerService issues automatically)")
            .RequireAuthorization("IncidentResponder")
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

        group.MapPost("/{id:guid}/postmortem/draft", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                var result = await sender.Send(new GenerateIncidentPostmortemDraftQuery(new IncidentId(id)), ct);
                return Results.Ok(result);
            })
            .WithName("GenerateIncidentPostmortemDraft")
            .WithSummary("AI-drafted postmortem for a resolved incident, via the Claude API — a draft for a " +
                         "human to review and edit, never a final artifact")
            .RequireAuthorization("IncidentResponder")
            .Produces<PostmortemDraftResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    /// <summary>
    /// No DeclaredByUserId/UserId/EscalatedByUserId field on any request body anymore — "who did
    /// this" comes from the authenticated JWT's claims (see ClaimsPrincipalExtensions.GetUserId),
    /// never from something the client could put in a JSON field. A client-supplied identity
    /// field is exactly the spoofing gap authentication exists to close.
    /// </summary>
    private sealed record DeclareIncidentRequest(string Title, Severity Severity, string AlertFingerprint);

    private sealed record ResolveIncidentBody(string Summary);

    private sealed record EscalateIncidentBody(string EscalatedTo);
}
