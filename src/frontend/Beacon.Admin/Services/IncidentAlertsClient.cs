using Microsoft.AspNetCore.SignalR.Client;

namespace Beacon.Admin.Services;

public sealed record IncidentAlert(Guid IncidentId, string Title, string Severity, DateTimeOffset ReceivedAt);

/// <summary>
/// Connects server-to-server, from this Blazor Server process's own backend to Beacon.Api's
/// IncidentAlertsHub — not from the end user's browser. That's *why* the hub can stay
/// unauthenticated and CORS-free (Program.cs's comment on Beacon.Api explains the same point
/// from the other side): a browser's same-origin/CORS rules never come into play for a
/// server-initiated connection like this one.
/// </summary>
public sealed class IncidentAlertsClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly List<IncidentAlert> _recent = [];

    public IReadOnlyList<IncidentAlert> RecentAlerts => _recent;
    public event Action? AlertReceived;

    public IncidentAlertsClient(IConfiguration configuration)
    {
        var baseUrl = configuration["BeaconApi:BaseUrl"] ?? "http://localhost:5252";

        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/incident-alerts")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<Guid, string, string>("IncidentDeclared", (incidentId, title, severity) =>
        {
            _recent.Insert(0, new IncidentAlert(incidentId, title, severity, DateTimeOffset.UtcNow));
            if (_recent.Count > 10) _recent.RemoveAt(_recent.Count - 1);
            AlertReceived?.Invoke();
        });
    }

    public async Task StartAsync()
    {
        if (_connection.State == HubConnectionState.Disconnected)
            await _connection.StartAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
