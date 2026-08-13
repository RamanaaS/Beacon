using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

/// <summary>
/// Same seam shape as IIncidentRepository/IRecentAlertsStore/IIncidentReportGenerator — Application
/// knows only "give me a draft for this incident," Infrastructure owns which LLM provider and API
/// shape sits behind that.
/// </summary>
public interface IPostmortemDraftGenerator
{
    Task<string> GenerateDraftAsync(Incident incident, CancellationToken ct = default);
}
