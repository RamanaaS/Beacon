using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Beacon.Api.Alerts;
using Beacon.Api.Auth;
using Beacon.Api.Common;
using Beacon.Api.Incidents;
using Beacon.Api.Status;
using Beacon.Api.Telemetry;
using Beacon.Application;
using Beacon.Infrastructure;
using Beacon.Infrastructure.Auth;
using Beacon.Infrastructure.Incidents;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// Structured logging (Phase 18) — sinks/levels read from appsettings.json's "Serilog" section
// (Serilog.Settings.Configuration) rather than hardcoded here, so console verbosity can differ
// from the file sink without a code change. Replaces the default Microsoft.Extensions.Logging
// console formatter entirely; UseSerilogRequestLogging() below adds one structured summary line
// per HTTP request (method, path, status, elapsed ms) on top of whatever each endpoint logs itself.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Application", "Beacon.Api"));

// Aspire's ServiceDefaults (Phase 15) — OpenTelemetry tracing/metrics, service discovery, and
// AddStandardResilienceHandler() on every outgoing HttpClient by default.
builder.AddServiceDefaults();

// Health checks (Phase 18) — a real, considered split, not "check everything and gate on all of
// it." SQL is tagged "ready": if the database is unreachable, this API genuinely cannot serve
// most requests, so /health should report Unhealthy and a real load balancer/orchestrator should
// stop routing traffic here. Redis is deliberately tagged "dependencies", NOT "ready" — Phase 13
// built RedisRecentAlertsStore specifically to keep serving requests (skipping dedup, not
// rejecting the alert) when Redis is down, so a health check that flipped /health to Unhealthy
// over a down Redis would pull healthy, working instances out of rotation for a failure mode the
// application was deliberately engineered to tolerate. Redis's real status is still visible in
// /health's response body (HealthCheckJsonWriter serializes every check, not just the aggregate)
// — a human or a dashboard can see it's degraded; a load balancer just doesn't act on it alone.
// The Redis check resolves the same IConnectionMultiplexer singleton AddInfrastructure registers
// (not a second, independently-connected one) — checking the actual connection the app uses,
// including its Phase 13 abortConnect=false/timeout tuning, rather than a fresh connection that
// would behave differently and prove nothing about the one that matters.
// The SQL connection string is resolved from IConfiguration when the check runs, not read off
// builder.Configuration up here: WebApplicationFactory's ConfigureAppConfiguration override isn't
// applied yet at this point, so an eager read made the integration tests health-check the *dev*
// BeaconDb instead of their own test database (and pass only on a machine where it existed).
builder.Services.AddHealthChecks()
    .AddSqlServer(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("BeaconDb")!, name: "sql", tags: ["ready"])
    .AddRedis(sp => sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>(), name: "redis", tags: ["dependencies"]);

// typeof(Program).Assembly added to the scan so MediatR also discovers notification handlers
// that live in Beacon.Api itself (IncidentDeclaredNotificationHandler bridges to SignalR) — see
// DependencyInjection.cs's own comment for why that handler can't live in Beacon.Application.
// Beacon.Infrastructure's own assembly is added for the same reason: ItsmTicketSyncHandler
// (Phase 13) needs IItsmBridgeClient, an Infrastructure-owned implementation.
builder.Services.AddApplication(typeof(Program).Assembly, typeof(Beacon.Infrastructure.DependencyInjection).Assembly);
builder.Services.AddInfrastructure(builder.Configuration);

// Backs GetPublicStatus's .CacheOutput() (Phase 12) — a real, in-process cache (no Redis needed)
// for the one endpoint that's explicitly supposed to be cheap to hammer: a public status page
// with no login wall has no rate limit to lean on the way every authenticated endpoint does.
builder.Services.AddOutputCache();

// Phase 19: /api/alerts/ingest and /ingest-raw are deliberately unauthenticated (see
// AlertEndpoints.cs's comment — no API key/mTLS mechanism exists yet for machine-to-machine
// callers) which also means they're the one pair of endpoints in this API with no login wall an
// attacker would have to get through first before hammering them. A rate limit is the pragmatic
// stopgap: caps how much load one caller can put on IngestAlertCommandHandler (and, transitively,
// the database and Redis dedup store) regardless of auth. Partitioned per client IP rather than
// one global bucket, so one noisy/malicious caller can't exhaust the whole limit for everyone else
// hitting the same endpoint.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("alert-ingestion", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 60,
                QueueLimit = 0
            }));
});

builder.Services.AddSingleton<WebSocketConnectionManager>();
builder.Services.AddHostedService<MetricsSimulatorService>();
builder.Services.AddHostedService<EscalationCheckerService>();

// Unauthenticated, like the telemetry WebSocket hub (Phase 4) and for the same reason — nothing
// crossing this channel (an incident id/title/severity) is sensitive enough to justify the extra
// complexity of authenticating a SignalR connection right now. Beacon.Admin (the one real
// consumer) connects server-to-server from its own backend, not from an end user's browser, so
// this isn't reachable directly from the public internet either.
builder.Services.AddSignalR();

// Read-only access needs any authenticated user; acting on an incident needs Responder or
// Admin. Viewer-only accounts (auditors, stakeholders watching a status page) can never declare,
// acknowledge, resolve, or escalate — enforced here, not just left as a documentation convention.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("IncidentResponder", policy => policy.RequireRole(Roles.Responder, Roles.Admin))
    .AddPolicy("AdminOnly", policy => policy.RequireRole(Roles.Admin));

// Named "DevCors" deliberately, not applied unconditionally — an allowed-origin list for a real
// deployed frontend is a production/deployment decision (Phase 18), not something to hardcode
// alongside the Vite dev server's localhost origin. Only the dev policy exists so far.
const string devCorsPolicy = "DevCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(devCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// Without this, System.Text.Json serializes/expects enums (Severity, IncidentStatus) as raw
// numbers — technically correct, but "severity": 4 is unreadable over the wire compared to
// "severity": "Sev1_Critical". A real, easy-to-forget default worth knowing about.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new IncidentIdJsonConverter());
});

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
// A Swagger UI "Authorize" button (paste a JWT, it's sent on every subsequent try-it-out call)
// is the usual next step here — skipped for now: Swashbuckle 10's SwaggerGen transitively pulls
// in Microsoft.OpenApi 2.x, which rewrote its whole model namespace (OpenApiSecurityScheme et al.
// moved/changed shape), and chasing that unfamiliar, freshly-broken API wasn't worth it for a
// convenience that doesn't affect whether auth actually works — every endpoint below still
// enforces the same JWT validation and role policy with or without a Swagger button for it.
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Correlation ID (Phase 18) — pushes the request's real W3C trace ID (Activity.Current, the same
// ID OpenTelemetry's spans use) into Serilog's LogContext, so every log line written anywhere
// during this request carries the identical ID a trace in the OTLP exporter would show. That's
// the actual point of a correlation ID: given one slow/failed request, "grep the logs for this ID"
// and "look up this trace" land on the exact same events, not two separately-numbered systems.
// Falls back to HttpContext.TraceIdentifier on the rare request with no active Activity (matches
// ASP.NET Core's own default request-tracking ID in that case, still unique per request).
//
// Registered BEFORE UseSerilogRequestLogging deliberately, not after: middleware registered
// earlier wraps everything registered later, so this has to be the outer layer for its
// LogContext.PushProperty scope to still be active when UseSerilogRequestLogging logs its own
// summary line on the way back out. Got this backwards on the first pass — the summary line's
// [CorrelationId] came out empty every time despite every other log line during the same request
// having it, caught by actually reading the real console output rather than trusting it compiled.
app.Use(async (context, next) =>
{
    var correlationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        await next();
    }
});

// One structured summary line per HTTP request (method, path, status code, elapsed ms) — inside
// the correlation-id middleware above so that line carries the same ID too.
app.UseSerilogRequestLogging();

app.UseExceptionHandler();
app.UseWebSockets();

if (app.Environment.IsDevelopment())
{
    app.UseCors(devCorsPolicy);
}

app.UseAuthentication();
app.UseAuthorization();

// Skipped under the integration-test host for the same reason as UseOutputCache below:
// IntegrationTestCollection.cs shares one BeaconApiFactory (one in-process host, one rate
// limiter instance) across the entire test collection, not one per test class. AlertEndpointsTests
// alone calls /api/alerts/ingest-raw several times per run; with the limiter active, unrelated
// tests elsewhere in the same collection accumulate against the same 60/min bucket and could start
// failing with 429 depending on run order and timing — a flaky-test bug, not a real safeguard.
// Real dev/production traffic always gets the limiter.
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseRateLimiter();
}

// Skipped under the integration-test host: WebApplicationFactory reuses one in-process server
// (and therefore one output-cache store) across every test in the collection, so a cached
// /api/status response from an earlier test would make a later "declare, then immediately
// re-read" assertion flaky. Real dev/production traffic always gets the cache.
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseOutputCache();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Convenience for local dev only — a real deployment applies migrations as an explicit
    // CI/CD pipeline step (Phase 17), not as a side effect of the app starting up.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<BeaconDbContext>().Database.MigrateAsync();

    // Roles have to exist before anyone can be assigned to one — same "dev convenience, real
    // deploy does this as a deliberate step" reasoning as the migration above.
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in Roles.All)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapIncidentEndpoints();
app.MapAlertEndpoints();
app.MapTelemetryEndpoints();
app.MapPublicStatusEndpoints();
app.MapHub<IncidentAlertsHub>("/hubs/incident-alerts");

// Readiness: only "ready"-tagged checks (SQL) gate the aggregate status — see the health-check
// registration above for why Redis deliberately doesn't. 503 here means "don't route traffic to
// this instance," matching what a real load balancer/Container Apps health probe would act on.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckJsonWriter.WriteResponse
}).ExcludeFromDescription();

// Liveness: just "is the process itself responsive," never gated on a downstream dependency —
// restarting this process would do nothing to fix a down SQL Server or Redis, so liveness
// shouldn't fail because of either.
app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = HealthCheckJsonWriter.WriteResponse
}).ExcludeFromDescription();

// A HealthCheckOptions.Predicate doesn't just gate the aggregate status — it decides which checks
// even *run* for that endpoint at all. Redis's "dependencies"-tagged check genuinely never appears
// in /health's or /alive's response, not just excluded from their pass/fail math (found by
// actually calling both live and noticing "redis" was simply missing from either body — the exact
// "look at the real output, don't assume the design worked" discipline this whole project has
// followed since Phase 9). /health/detail has no predicate at all, so it's every registered check
// — SQL, Redis, and self — regardless of which ones happen to gate traffic routing. The endpoint
// a human or a dashboard actually wants when asking "what's going on with this instance," as
// opposed to the narrow yes/no a load balancer polls.
app.MapHealthChecks("/health/detail", new HealthCheckOptions
{
    ResponseWriter = HealthCheckJsonWriter.WriteResponse
}).ExcludeFromDescription();

app.Run();

// Exposed so Beacon.IntegrationTests can spin the API up in-process via WebApplicationFactory<Program>.
public partial class Program;
