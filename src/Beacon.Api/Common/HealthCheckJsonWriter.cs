using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Beacon.Api.Common;

/// <summary>
/// The default ASP.NET Core health check response is a bare "Healthy"/"Unhealthy" string — enough
/// to gate a load balancer, not enough to actually diagnose anything from. This writes each
/// individual check's name/status/description/duration, so a degraded dependency (Redis, in this
/// project's actual running state — see RedisRecentAlertsStore's Phase 13 comment) is visible in
/// the response body even on the /health checks that deliberately don't let it flip the overall
/// HTTP status code (see Program.cs's "ready" vs. "dependencies" tag split).
/// </summary>
public static class HealthCheckJsonWriter
{
    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds,
                tags = entry.Value.Tags
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
