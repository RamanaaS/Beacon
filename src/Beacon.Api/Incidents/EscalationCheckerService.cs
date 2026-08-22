using Beacon.Application.Common;
using Beacon.Application.Incidents;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Api.Incidents;

/// <summary>
/// Scans open incidents on a timer and issues the same EscalateIncidentCommand a human hitting
/// POST /api/incidents/{id}/escalate would trigger — this is the actual payoff of routing
/// escalation through MediatR: the checker doesn't touch Incident or IIncidentRepository's write
/// path directly, it just sends a command, identically to the API. IIncidentRepository and ISender
/// are scoped (EF Core's DbContext is scoped), so this singleton BackgroundService creates a
/// fresh scope every tick rather than holding one for its own lifetime.
///
/// Phase 19: the loop body is wrapped in try/catch. BackgroundService.ExecuteAsync throwing is
/// fatal to the whole Generic Host by default (BackgroundServiceExceptionBehavior.StopHost) - a
/// SqlException from one tick's GetOpenIncidentsAsync call used to take down the entire API
/// process, not just this service, confirmed live by forcing BeaconDb offline while the app was
/// running and watching it stop responding entirely. One tick failing to reach the database
/// should log and wait for the next tick, not kill incident declaration, auth, and every other
/// endpoint along with it.
/// </summary>
public sealed class EscalationCheckerService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<EscalationCheckerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Checks well inside the shortest escalation threshold (5 minutes for Sev1, see
        // EscalationPolicy) so a due incident doesn't sit for most of that window before anyone
        // finds out — 30s in a real system might be a minute or two; short here for demoability.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var openIncidents = await repository.GetOpenIncidentsAsync(stoppingToken);
                var now = clock.UtcNow;

                foreach (var incident in openIncidents)
                {
                    if (!EscalationPolicy.IsEscalationDue(incident, now))
                        continue;

                    logger.LogWarning(
                        "Incident {IncidentId} ({Severity}) unacknowledged past its escalation threshold — escalating",
                        incident.Id, incident.CurrentSeverity);

                    await sender.Send(
                        new EscalateIncidentCommand(incident.Id, "system:escalation-checker", "secondary-oncall"),
                        stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Escalation check tick failed — will retry on the next tick instead of crashing the host");
            }
        }
    }
}
