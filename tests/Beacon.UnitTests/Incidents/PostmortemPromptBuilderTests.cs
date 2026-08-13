using Beacon.Application.Incidents;
using Beacon.Domain.Common;
using Beacon.Domain.Incidents;
using FluentAssertions;

namespace Beacon.UnitTests.Incidents;

public class PostmortemPromptBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Prompt_includes_incident_title_severity_and_status()
    {
        var incident = Incident.Declare("Payments API returning 500s", Severity.Sev1_Critical, "user-1", "fp-1", Now);

        var prompt = PostmortemPromptBuilder.Build(incident);

        prompt.Should().Contain("Payments API returning 500s");
        prompt.Should().Contain("Sev1_Critical");
        prompt.Should().Contain("Declared");
    }

    [Fact]
    public void Prompt_includes_every_event_in_the_timeline_in_order()
    {
        var incident = Incident.Declare("DB down", Severity.Sev1_Critical, "user-1", "fp-1", Now);
        incident.Acknowledge("user-7", Now.AddMinutes(2));
        incident.Resolve("user-7", "Restarted the connection pool.", Now.AddMinutes(10));

        var prompt = PostmortemPromptBuilder.Build(incident);

        var declaredIndex = prompt.IndexOf("[Declared]", StringComparison.Ordinal);
        var acknowledgedIndex = prompt.IndexOf("[Acknowledged]", StringComparison.Ordinal);
        var resolvedIndex = prompt.IndexOf("[Resolved]", StringComparison.Ordinal);

        declaredIndex.Should().BeGreaterThan(-1);
        acknowledgedIndex.Should().BeGreaterThan(declaredIndex);
        resolvedIndex.Should().BeGreaterThan(acknowledgedIndex);
        prompt.Should().Contain("Restarted the connection pool.");
    }

    [Fact]
    public void Prompt_instructs_the_model_not_to_invent_details_and_to_flag_insufficient_information()
    {
        var incident = Incident.Declare("Minor blip", Severity.Sev4_Low, "user-1", "fp-1", Now);

        var prompt = PostmortemPromptBuilder.Build(incident);

        prompt.Should().Contain("Do not invent details");
        prompt.Should().Contain("DRAFT");
    }
}
