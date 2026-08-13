using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

public sealed record GenerateIncidentExcelReportQuery(IncidentId IncidentId) : IRequest<byte[]>;

public sealed class GenerateIncidentExcelReportQueryHandler(
    IIncidentRepository repository,
    IIncidentReportGenerator reportGenerator) : IRequestHandler<GenerateIncidentExcelReportQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateIncidentExcelReportQuery request, CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, cancellationToken)
            ?? throw new IncidentNotFoundException(request.IncidentId);

        return reportGenerator.GenerateExcelReport(incident);
    }
}

public sealed record GenerateIncidentPdfReportQuery(IncidentId IncidentId) : IRequest<byte[]>;

public sealed class GenerateIncidentPdfReportQueryHandler(
    IIncidentRepository repository,
    IIncidentReportGenerator reportGenerator) : IRequestHandler<GenerateIncidentPdfReportQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateIncidentPdfReportQuery request, CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, cancellationToken)
            ?? throw new IncidentNotFoundException(request.IncidentId);

        return reportGenerator.GeneratePdfReport(incident);
    }
}
