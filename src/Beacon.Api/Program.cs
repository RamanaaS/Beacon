using System.Text.Json.Serialization;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

app.UseExceptionHandler();
app.UseWebSockets();

if (app.Environment.IsDevelopment())
{
    app.UseCors(devCorsPolicy);
}

app.UseAuthentication();
app.UseAuthorization();

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

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("HealthCheck")
   .ExcludeFromDescription();

app.Run();

// Exposed so Beacon.IntegrationTests can spin the API up in-process via WebApplicationFactory<Program>.
public partial class Program;
