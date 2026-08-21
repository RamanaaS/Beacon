using Beacon.Application.Alerts;
using Beacon.Domain.Common;
using Beacon.Infrastructure.Alerts;
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

        // A second, deliberately different ingestion shape from /ingest above: this one accepts
        // one raw pipe-delimited text line (native/log-parser-shim/log_parser.h documents the
        // format) instead of structured JSON — standing in for a high-volume raw webhook body,
        // the case the native C parser (Phase 13) actually exists for. Parsing happens here, at
        // the edge; once parsed it becomes exactly the same IngestAlertCommand the JSON path
        // already sends, so IngestAlertCommandHandler has no idea which endpoint was called.
        group.MapPost("/ingest-raw", async (
                IngestRawAlertRequest request,
                ISender sender,
                CancellationToken ct) =>
            {
                var parsed = LogParserShim.Parse(request.RawLine);
                if (parsed is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["rawLine"] = ["Malformed raw alert line — expected severity|source|alertName|instance|message."]
                    });

                if (!Enum.TryParse<Severity>(parsed.Severity, out var severity))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["rawLine"] = [$"Unrecognized severity '{parsed.Severity}'."]
                    });

                var command = new IngestAlertCommand(parsed.Source, parsed.AlertName, parsed.Instance, severity, parsed.Message);
                var result = await sender.Send(command, ct);
                return Results.Ok(result);
            })
            .WithName("IngestRawAlert")
            .WithSummary("Ingest a raw pipe-delimited alert line, parsed by the native log-parser-shim (Phase 13)")
            .Produces<IngestAlertResult>()
            .ProducesValidationProblem();

        return group;
    }

    private sealed record IngestRawAlertRequest(string RawLine);
}
