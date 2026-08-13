using Beacon.Application.Common;
using Beacon.Domain.Incidents;
using MediatR;

namespace Beacon.Application.Incidents;

public sealed record GenerateIncidentPostmortemDraftQuery(IncidentId IncidentId) : IRequest<PostmortemDraftResponse>;

public sealed record PostmortemDraftResponse(string Draft, DateTimeOffset GeneratedAt);

public sealed class GenerateIncidentPostmortemDraftQueryHandler(
    IIncidentRepository repository,
    IPostmortemDraftGenerator generator,
    IClock clock) : IRequestHandler<GenerateIncidentPostmortemDraftQuery, PostmortemDraftResponse>
{
    public async Task<PostmortemDraftResponse> Handle(
        GenerateIncidentPostmortemDraftQuery request, CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, cancellationToken)
            ?? throw new IncidentNotFoundException(request.IncidentId);

        // Postmortems are a post-incident artifact by definition — drafting one for an incident
        // that's still open would either be premature or, worse, would let the model narrate an
        // ending that hasn't happened yet.
        if (incident.CurrentStatus != IncidentStatus.Resolved)
            throw new InvalidOperationException(
                $"Incident {incident.Id} is not resolved yet — postmortems can only be drafted for resolved incidents.");

        var draft = await generator.GenerateDraftAsync(incident, cancellationToken);

        return new PostmortemDraftResponse(draft, clock.UtcNow);
    }
}
