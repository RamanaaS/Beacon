using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Beacon.Infrastructure.Auth;

public interface IJwtTokenGenerator
{
    (string Token, DateTimeOffset ExpiresAt) GenerateToken(string userId, string email, IEnumerable<string> roles);
}

/// <summary>
/// Lives entirely in Infrastructure, with no Application-layer interface — unlike
/// IIncidentRepository/IRecentAlertsStore. Auth isn't part of the incident bounded context CQRS
/// was scoped to (ARCHITECTURE.md Section 9); Beacon.Api calls this directly, the same way it
/// calls UserManager/SignInManager directly for register/login, rather than routing identity
/// concerns through MediatR.
/// </summary>
public sealed class JwtTokenGenerator(IOptions<JwtSettings> options) : IJwtTokenGenerator
{
    public (string Token, DateTimeOffset ExpiresAt) GenerateToken(string userId, string email, IEnumerable<string> roles)
    {
        var settings = options.Value;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(settings.ExpiryMinutes);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ..roles.Select(role => new Claim(ClaimTypes.Role, role))
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
