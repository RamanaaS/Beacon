namespace Beacon.Application.Incidents;

/// <summary>
/// Owned by Application, implemented by Infrastructure — same seam discipline as
/// IIncidentRepository. itsm-bridge (src/itsm-bridge, Java Spring Boot) is the one deliberate
/// service boundary in this project (ARCHITECTURE.md): a real HTTP call to a separately-owned,
/// separately-deployed process, not another in-process abstraction.
/// </summary>
public interface IItsmBridgeClient
{
    Task CreateTicketAsync(string incidentId, string title, string severity, CancellationToken ct = default);

    Task CloseTicketAsync(string incidentId, CancellationToken ct = default);
}
