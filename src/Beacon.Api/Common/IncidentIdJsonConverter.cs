using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Domain.Incidents;

namespace Beacon.Api.Common;

/// <summary>
/// Without this, System.Text.Json serializes IncidentId (a record struct wrapping Guid) as its
/// own shape — {"value": "..."} — instead of a plain GUID string, which breaks round-tripping
/// an id from a response straight back into a URL. Strongly-typed IDs need this converter
/// (once, registered globally) as the price of the primitive-obsession protection they buy —
/// see LEARNING_GUIDE.md Phase 1 for why IncidentId exists in the first place.
/// </summary>
public sealed class IncidentIdJsonConverter : JsonConverter<IncidentId>
{
    public override IncidentId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, IncidentId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
