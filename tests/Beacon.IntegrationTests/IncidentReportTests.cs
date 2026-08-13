using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using OfficeOpenXml;
using PdfSharp.Pdf.IO;

namespace Beacon.IntegrationTests;

/// <summary>
/// Round-trips the actual generated files instead of just asserting "some bytes came back" —
/// re-opening the Excel workbook with EPPlus and the PDF with PdfSharp's own reader is what
/// would have caught a report generator that produces a file Excel/Acrobat can't actually open,
/// which byte-count or content-type assertions alone would miss entirely.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IncidentReportTests(BeaconApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Excel_report_contains_the_incident_summary_and_full_timeline()
    {
        var id = await DeclareAsync("Report test incident", "fp-report-xlsx");
        (await _client.PostAsJsonAsync($"/api/incidents/{id}/acknowledge", new { userId = "user-7" }))
            .EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/incidents/{id}/report.xlsx");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await response.Content.ReadAsByteArrayAsync();

        ExcelPackage.License.SetNonCommercialPersonal("Beacon Tests");
        using var package = new ExcelPackage(new MemoryStream(bytes));

        var summary = package.Workbook.Worksheets["Summary"];
        summary.Should().NotBeNull();
        summary!.Cells[2, 2].Text.Should().Be("Report test incident");

        var timeline = package.Workbook.Worksheets["Timeline"];
        timeline.Should().NotBeNull();
        timeline!.Cells[2, 2].Text.Should().Be("Declared");
        timeline.Cells[3, 2].Text.Should().Be("Acknowledged");
    }

    [Fact]
    public async Task Pdf_report_is_a_valid_pdf_with_at_least_one_page()
    {
        var id = await DeclareAsync("PDF report test incident", "fp-report-pdf");

        var response = await _client.GetAsync($"/api/incidents/{id}/report.pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        document.PageCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Report_for_unknown_incident_returns_404()
    {
        var response = await _client.GetAsync($"/api/incidents/{Guid.NewGuid()}/report.xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> DeclareAsync(string title, string fingerprint)
    {
        var response = await _client.PostAsJsonAsync("/api/incidents", new
        {
            title,
            severity = "Sev1_Critical",
            declaredByUserId = "user-42",
            alertFingerprint = fingerprint
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedIncidentDto>();
        return created!.Id;
    }

    private sealed record CreatedIncidentDto(Guid Id);
}
