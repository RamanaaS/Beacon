using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Beacon.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Every incident-mutating endpoint uses this instead of trusting a userId the client puts
    /// in the request body — a client claiming to be someone else in a JSON field is exactly the
    /// spoofing gap authentication exists to close. The "who did this" behind every IncidentEvent
    /// now comes from a cryptographically verified JWT claim, not user-supplied input.
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated principal has no 'sub' claim.");
}
