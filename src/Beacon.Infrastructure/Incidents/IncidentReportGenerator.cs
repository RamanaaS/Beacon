using Beacon.Application.Incidents;
using Beacon.Domain.Incidents;
using OfficeOpenXml;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// EPPlus for Excel, PDFsharp for PDF — the same two libraries the resume's report-automation
/// bullets are built on, generating a real postmortem report (summary + full timeline) instead
/// of a toy example. Both write to an in-memory MemoryStream and return bytes rather than
/// writing to disk — Blob Storage (Phase 18, Azure) is what actually persists these; this class
/// has no opinion about where the bytes end up.
/// </summary>
public sealed class IncidentReportGenerator : IIncidentReportGenerator
{
    static IncidentReportGenerator()
    {
        // EPPlus 5+ requires an explicit license declaration before any ExcelPackage is
        // constructed, or it throws at runtime. Free for noncommercial use under the Polyform
        // Noncommercial license — exactly this project's situation (portfolio, not sold).
        ExcelPackage.License.SetNonCommercialPersonal("Beacon Project");

        // PDFsharp 6's cross-platform core needs an explicit font resolver — see
        // WindowsFontResolver's own comment for why this is a real, not incidental, constraint.
        GlobalFontSettings.FontResolver = new WindowsFontResolver();
    }

    public byte[] GenerateExcelReport(Incident incident)
    {
        using var package = new ExcelPackage();

        var summary = package.Workbook.Worksheets.Add("Summary");
        summary.Cells[1, 1].Value = "Incident ID";
        summary.Cells[1, 2].Value = incident.Id.Value.ToString();
        summary.Cells[2, 1].Value = "Title";
        summary.Cells[2, 2].Value = incident.Title;
        summary.Cells[3, 1].Value = "Severity";
        summary.Cells[3, 2].Value = incident.CurrentSeverity.ToString();
        summary.Cells[4, 1].Value = "Status";
        summary.Cells[4, 2].Value = incident.CurrentStatus.ToString();
        summary.Cells[5, 1].Value = "Declared At (UTC)";
        summary.Cells[5, 2].Value = incident.DeclaredAt.UtcDateTime;
        summary.Cells[5, 2].Style.Numberformat.Format = "yyyy-mm-dd hh:mm:ss";
        summary.Cells[6, 1].Value = "Escalated";
        summary.Cells[6, 2].Value = incident.IsEscalated ? "Yes" : "No";
        summary.Cells[7, 1].Value = "Responders";
        summary.Cells[7, 2].Value = string.Join(", ", incident.Responders);
        summary.Column(1).Style.Font.Bold = true;
        summary.Cells.AutoFitColumns();

        var timeline = package.Workbook.Worksheets.Add("Timeline");
        timeline.Cells[1, 1].Value = "Timestamp (UTC)";
        timeline.Cells[1, 2].Value = "Event";
        timeline.Cells[1, 3].Value = "Actor";
        timeline.Cells[1, 4].Value = "Details";
        timeline.Cells[1, 1, 1, 4].Style.Font.Bold = true;

        var row = 2;
        foreach (var e in incident.Events.OrderBy(e => e.OccurredAt))
        {
            timeline.Cells[row, 1].Value = e.OccurredAt.UtcDateTime;
            timeline.Cells[row, 1].Style.Numberformat.Format = "yyyy-mm-dd hh:mm:ss";
            timeline.Cells[row, 2].Value = IncidentEventFormatter.EventTypeLabel(e);
            timeline.Cells[row, 3].Value = IncidentEventFormatter.Actor(e);
            timeline.Cells[row, 4].Value = IncidentEventFormatter.Describe(e);
            row++;
        }

        timeline.Cells.AutoFitColumns();

        return package.GetAsByteArray();
    }

    public byte[] GeneratePdfReport(Incident incident)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);

        var titleFont = new XFont("Arial", 18, XFontStyleEx.Bold);
        var headerFont = new XFont("Arial", 12, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 10, XFontStyleEx.Regular);

        const double margin = 40;
        const double lineHeight = 16;
        double y = margin;

        gfx.DrawString("Incident Postmortem Report", titleFont, XBrushes.Black, new XPoint(margin, y));
        y += 30;

        void DrawLine(string text, XFont? font = null)
        {
            gfx.DrawString(text, font ?? bodyFont, XBrushes.Black, new XPoint(margin, y));
            y += lineHeight;
        }

        DrawLine($"Incident ID: {incident.Id}");
        DrawLine($"Title: {incident.Title}");
        DrawLine($"Severity: {incident.CurrentSeverity}");
        DrawLine($"Status: {incident.CurrentStatus}");
        DrawLine($"Declared: {incident.DeclaredAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        DrawLine($"Escalated: {(incident.IsEscalated ? "Yes" : "No")}");
        DrawLine($"Responders: {(incident.Responders.Count > 0 ? string.Join(", ", incident.Responders) : "none")}");

        y += 14;
        DrawLine("Timeline", headerFont);
        y += 4;

        foreach (var e in incident.Events.OrderBy(e => e.OccurredAt))
        {
            if (y > page.Height.Point - 60)
            {
                page = document.AddPage();
                gfx = XGraphics.FromPdfPage(page);
                y = margin;
            }

            var line = $"{e.OccurredAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC — " +
                       $"{IncidentEventFormatter.EventTypeLabel(e)} ({IncidentEventFormatter.Actor(e)}): " +
                       $"{IncidentEventFormatter.Describe(e)}";
            DrawLine(line);
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
