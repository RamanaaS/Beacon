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
        services.AddClaudeIntegration(configuration);
        services.AddItsmBridgeIntegration(configuration);

        return services;
    }

    private static void AddItsmBridgeIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var itsmSettings = configuration.GetSection("ItsmBridge").Get<ItsmBridgeSettings>() ?? new ItsmBridgeSettings();

        // Same resilience approach as AddClaudeIntegration (ARCHITECTURE.md Table B names both
        // calls explicitly), but with the defaults tightened way down. AddStandardResilienceHandler
        // ships defaults sized for a genuine third-party API (multiple retries with backoff, a
        // ~30s total timeout) — measured against this project's own itsm-bridge simply not running
        // in local dev, that turned the full integration-test suite from ~20s to over 6 minutes,
        // every declare/resolve call quietly paying the full retry budget before
        // ItsmTicketSyncHandler's catch block ever got a chance to swallow the failure.
        // itsm-bridge is a same-network internal service, not a dependency worth retrying hard for.
        //
        // RemoveAllResilienceHandlers() first: Phase 15's Beacon.ServiceDefaults.AddServiceDefaults()
        // calls ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler()), which
        // applies its own untouched-default resilience pipeline to every HttpClient in the app,
        // including this one. Without removing it first, the tuned pipeline below stacks *on top
        // of* that untouched one instead of replacing it — measured live: 20s per call instead of
        // the ~3s this same tuning produced before ServiceDefaults existed. Two resilience layers
        // isn't "more resilient," it's the same latency bug from earlier this phase, reintroduced
        // by a different piece of code.
        var itsmBridgeClientBuilder = services.AddHttpClient<IItsmBridgeClient, ItsmBridgeClient>(client =>
            {
                client.BaseAddress = new Uri(itsmSettings.BaseUrl);
            });

#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental but is Microsoft's own documented
                                    // way to reconfigure a client's pipeline instead of stacking a second one on top
                                    // of ServiceDefaults' blanket default — see the comment above for why that matters.
        itsmBridgeClientBuilder
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 1;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(3);

                // The library defaults (FailureRatio 0.1 over MinimumThroughput 100 samples) never
                // trip at this call volume — every single request would keep paying the full retry
                // + timeout cost independently instead of the circuit ever actually opening. Same
                // "two failures is enough to know it's down" reasoning as RedisRecentAlertsStore's
                // breaker earlier this phase.
                //
                // SamplingDuration has to be comfortably longer than TotalRequestTimeout, not just
                // "some short value" — Polly's circuit breaker only counts failures inside its
                // current rolling window. A 2s window against a request that can itself take up to
                // 3s to fail meant two sequential failures almost never landed in the same window,
                // so the breaker never saw 2 failures at once and never opened — measured live,
                // every request kept independently paying the full ~3s instead of only the first
                // couple. 10s comfortably covers several sequential failing requests.
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                options.CircuitBreaker.MinimumThroughput = 2;
                options.CircuitBreaker.FailureRatio = 1.0;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });
#pragma warning restore EXTEXP0001
    }

    private static void AddClaudeIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClaudeSettings>(configuration.GetSection("Claude"));
        var claudeSettings = configuration.GetSection("Claude").Get<ClaudeSettings>() ?? new ClaudeSettings();

        // AddStandardResilienceHandler is the current (2024+) way to attach Polly to an
        // HttpClient — it supersedes the older Microsoft.Extensions.Http.Polly package's
        // AddPolicyHandler style. One call gives retry (with jittered exponential backoff),
        // a circuit breaker, and both a per-attempt and a total-request timeout — exactly the
        // resilience ARCHITECTURE.md named for "the LLM API call," a real external dependency
        // that can fail or rate-limit independently of Beacon itself.
        // RemoveAllResilienceHandlers() before re-adding — see the identical comment in
        // AddItsmBridgeIntegration for why: Beacon.ServiceDefaults.AddServiceDefaults() (Phase 15)
        // already puts a default resilience handler on every HttpClient app-wide, so without
        // removing it first this would double-stack rather than reconfigure.
#pragma warning disable EXTEXP0001 // see AddItsmBridgeIntegration's identical suppression for why
        services.AddHttpClient<IPostmortemDraftGenerator, ClaudePostmortemDraftGenerator>(client =>
            {
                client.BaseAddress = new Uri(claudeSettings.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler();
#pragma warning restore EXTEXP0001
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
