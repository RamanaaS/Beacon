namespace Beacon.Domain.Incidents;

public readonly record struct IncidentId(Guid Value)
{
    public static IncidentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
