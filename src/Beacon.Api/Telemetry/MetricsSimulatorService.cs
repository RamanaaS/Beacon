using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Beacon.Api.Telemetry;

/// <summary>
/// Stands in for real service instrumentation for now — generates plausible metric samples for
/// a fixed set of services on a timer and broadcasts them over the WebSocket hub. This is the
/// right-sized version for this phase: a separate simulator process talking to the API over its
/// own protocol is real complexity that isn't justified until something concrete needs it (a
/// second consumer, load generation independent of the API process). Runs in-process instead.
/// </summary>
public sealed class MetricsSimulatorService(
    WebSocketConnectionManager connections,
    IOptions<JsonOptions> jsonOptions,
    ILogger<MetricsSimulatorService> logger) : BackgroundService
{
    private static readonly string[] Services = ["payments-api", "checkout-api", "auth-api"];
    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Metrics simulator started, simulating {Count} services", Services.Length);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (connections.ConnectionCount == 0)
                continue; // no point generating load nobody's listening to

            foreach (var service in Services)
            {
                var sample = new MetricSample(
                    service,
                    CpuPercent: Math.Round(20 + _random.NextDouble() * 60, 1),
                    LatencyMs: Math.Round(50 + _random.NextDouble() * 200, 1),
                    ErrorRatePercent: Math.Round(_random.NextDouble() * 2, 2),
                    Timestamp: DateTimeOffset.UtcNow);

                // Reuses the same JsonOptions Program.cs configures for the REST API (enum-as-
                // string, camelCase) — one wire-format source of truth instead of a second,
                // silently different default from a bare JsonSerializer.Serialize(sample) call.
                var json = JsonSerializer.Serialize(sample, jsonOptions.Value.SerializerOptions);
                await connections.BroadcastAsync(json, stoppingToken);
            }
        }
    }
}
