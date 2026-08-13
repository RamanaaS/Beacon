using Beacon.Domain.Incidents;

namespace Beacon.Application.Incidents;

/// <summary>
/// Owned by Application, implemented by Infrastructure — same seam shape as
/// IIncidentRepository/IRecentAlertsStore. The report format (EPPlus vs. PDFsharp, or a future
/// swap to something else) is an infrastructure detail; Application only needs "give me bytes
/// for this incident."
/// </summary>
public interface IIncidentReportGenerator
{
    byte[] GenerateExcelReport(Incident incident);

    byte[] GeneratePdfReport(Incident incident);
}
