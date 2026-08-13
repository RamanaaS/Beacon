namespace Beacon.Infrastructure.Auth;

/// <summary>
/// Bound from configuration ("Jwt" section). The dev SigningKey in appsettings.json is exactly
/// that — a dev placeholder, committed to source control, which is normally a real security
/// problem. It's acceptable here only because Phase 18 (Azure) is the point this project actually
/// gets a real secrets story (Key Vault); flagging that now rather than pretending the dev
/// default is production-ready.
/// </summary>
public sealed class JwtSettings
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string SigningKey { get; init; }
    public int ExpiryMinutes { get; init; } = 60;
}
