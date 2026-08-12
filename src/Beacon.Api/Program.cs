using System.Text.Json.Serialization;
using Beacon.Api.Alerts;
using Beacon.Api.Common;
using Beacon.Api.Incidents;
using Beacon.Application;
using Beacon.Infrastructure;
using Beacon.Infrastructure.Incidents;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Convenience for local dev only — a real deployment applies migrations as an explicit
    // CI/CD pipeline step (Phase 17), not as a side effect of the app starting up.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<BeaconDbContext>().Database.MigrateAsync();
}

app.MapIncidentEndpoints();
app.MapAlertEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("HealthCheck")
   .ExcludeFromDescription();

app.Run();

// Exposed so Beacon.IntegrationTests can spin the API up in-process via WebApplicationFactory<Program>.
public partial class Program;
