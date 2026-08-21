namespace Beacon.StatusPage.Services;

// Mirrors Beacon.Api's GetPublicStatus response shape exactly (Beacon.Application/Incidents/
// GetPublicStatus.cs) — deliberately a separate set of types, not a shared library reference,
// same reasoning as every other frontend in this project: this is what actually crosses the wire,
// and a shared-assembly reference would hide a real serialization break behind a compile-time
// contract nothing here actually sends.
public sealed record PublicComponentStatus(string Name, string Status);

public sealed record PublicIncidentSummary(string Title, string Severity, string Status, DateTimeOffset DeclaredAt);

public sealed record UptimeDay(DateOnly Date, bool HadMajorIncident);

public sealed record PublicStatusResponse(
    string OverallStatus,
    IReadOnlyList<PublicComponentStatus> Components,
    IReadOnlyList<PublicIncidentSummary> ActiveIncidents,
    IReadOnlyList<UptimeDay> UptimeHistory);
