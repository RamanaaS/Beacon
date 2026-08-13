using System.Text;
using Beacon.Application.Alerts;
using Beacon.Application.Common;
using Beacon.Application.Incidents;
using Beacon.Infrastructure.Alerts;
using Beacon.Infrastructure.Auth;
using Beacon.Infrastructure.Common;
using Beacon.Infrastructure.Incidents;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace Beacon.Infrastructure;

/// <summary>
/// Keeps Beacon.Api from needing a direct reference to EF Core, the SQL Server provider,
/// StackExchange.Redis, or the JWT/Identity packages — it depends only on this method's
/// signature and app.UseAuthentication()/UseAuthorization() (both part of the shared ASP.NET
/// Core framework, not an extra package).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BeaconDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("BeaconDb")));

        services.AddScoped<IIncidentRepository, IncidentRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IIncidentReportGenerator, IncidentReportGenerator>();

        // ConnectionMultiplexer.Connect normally throws immediately if Redis isn't reachable —
        // abortConnect=false (set on the connection string) lets the app start regardless and
        // retries in the background, so a Redis outage degrades only the alert-ingestion
        // endpoint that actually needs it, not the whole API.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis")!));
        services.AddSingleton<IRecentAlertsStore, RedisRecentAlertsStore>();

        services.AddAuthInfrastructure(configuration);

        return services;
    }

    private static void AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // AddIdentityCore, not AddIdentity — this is an API with no server-rendered login page
        // and no cookie-based sign-in, so the parts of AddIdentity built for that (the cookie
        // authentication scheme, external-login plumbing) would be dead weight here.
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<BeaconDbContext>()
            .AddSignInManager();

        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()
            ?? throw new InvalidOperationException("Missing required 'Jwt' configuration section.");

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // By default JwtSecurityTokenHandler silently remaps short claim names to long
                // ClaimTypes URIs on the way in (e.g. "sub" -> ClaimTypes.NameIdentifier) — a
                // well-known ASP.NET Core gotcha where the claim you read back doesn't match the
                // claim name you wrote. Disabling it keeps "sub" as "sub", matching what
                // JwtTokenGenerator actually put in the token.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization();
    }
}
