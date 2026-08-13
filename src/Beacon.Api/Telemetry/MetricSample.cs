namespace Beacon.Api.Telemetry;

public sealed record MetricSample(
    string ServiceName,
    double CpuPercent,
    double LatencyMs,
    double ErrorRatePercent,
    DateTimeOffset Timestamp);
