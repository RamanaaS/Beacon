namespace Beacon.Infrastructure.Auth;

/// <summary>
/// Viewer can read; Responder can act on incidents; Admin is Responder plus (eventually) user
/// management. Deliberately just three flat roles, not a permissions matrix — nothing in this
/// project yet needs finer granularity than "can you act on incidents or not."
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Responder = "Responder";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Admin, Responder, Viewer];
}
