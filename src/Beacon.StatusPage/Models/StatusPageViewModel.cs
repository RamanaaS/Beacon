using Beacon.StatusPage.Services;

namespace Beacon.StatusPage.Models;

/// <summary>
/// IsAvailable=false is a real, expected state, not an error path bolted on for completeness: a
/// public status page's whole job is to still say *something* coherent when Beacon.Api itself is
/// down — the exact moment a status page matters most is also the moment its usual data source is
/// most likely to be unreachable. A page that 500s under that condition has failed at its one job.
/// </summary>
public sealed record StatusPageViewModel(bool IsAvailable, PublicStatusResponse? Status)
{
    public static StatusPageViewModel Unavailable() => new(false, null);
    public static StatusPageViewModel Available(PublicStatusResponse status) => new(true, status);
}
