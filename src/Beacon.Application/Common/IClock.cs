namespace Beacon.Application.Common;

/// <summary>
/// The seam Phase 1 was built for: every Incident behavior method takes occurredAt as an
/// explicit parameter instead of calling DateTimeOffset.UtcNow internally, specifically so
/// this can be injected here rather than the domain reaching for the system clock itself.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
