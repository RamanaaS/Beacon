namespace Beacon.Application.Common;

/// <summary>
/// Distinct from IncidentNotFoundException/InvalidOperationException — "this endpoint needs an
/// external API key nobody has configured" is a deployment/configuration problem, not a domain
/// state conflict, and deserves its own HTTP status (503, mapped in DomainExceptionHandler) rather
/// than being lumped in with the 409s a real domain-rule violation produces.
/// </summary>
public sealed class ExternalServiceNotConfiguredException(string serviceName, string configurationHint)
    : Exception($"{serviceName} is not configured. {configurationHint}");
