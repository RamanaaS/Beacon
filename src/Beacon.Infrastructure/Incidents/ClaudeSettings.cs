namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// ApiKey is deliberately left empty by default (see appsettings.json) rather than given a dev
/// placeholder the way Jwt:SigningKey was in Phase 7. A JWT signing key only needs to be
/// internally consistent for local dev to work at all; a Claude API key is a real external,
/// billable credential — there is no honest placeholder for it. It's expected to come from
/// `dotnet user-secrets` locally or Key Vault/environment in a real deployment, never committed.
/// </summary>
public sealed class ClaudeSettings
{
    public string ApiKey { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.anthropic.com";
    public string Model { get; init; } = "claude-sonnet-5";
    public int MaxTokens { get; init; } = 1024;
}
