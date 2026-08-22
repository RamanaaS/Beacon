using Beacon.Application.Common;
using Beacon.Application.Incidents;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Common;

/// <summary>
/// Translates domain/application exceptions into ProblemDetails responses instead of leaking a
/// raw 500 + stack trace for expected failure modes — and, as of Phase 19, everything *else* too.
/// The original version only handled the four cases below and let anything unmatched fall through
/// to ASP.NET Core's default behavior — which, with no developer-exception-page registered, meant
/// an unexpected exception (a SqlException from an unreachable database, say) could genuinely
/// leak an internal exception message, a connection string fragment, or a stack trace straight to
/// the client. A real information-disclosure risk, not just an unhandled edge case — confirmed by
/// actually stopping the local SQL Server instance and hitting a real endpoint (LEARNING_GUIDE.md's
/// Phase 19 entry). Every exception now gets a clean ProblemDetails response; only the ones
/// matched below get a specific status/title, everything else becomes a generic 500 with the real
/// exception detail logged server-side (picked up by Serilog, correlation-ID-enriched same as
/// every other log line for the request) but never sent to the caller outside Development.
/// </summary>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            IncidentNotFoundException => (StatusCodes.Status404NotFound, "Incident not found"),
            ExternalServiceNotConfiguredException => (StatusCodes.Status503ServiceUnavailable, "External service not configured"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Incident state conflict"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        var isUnexpected = statusCode == StatusCodes.Status500InternalServerError;
        if (isUnexpected)
        {
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = isUnexpected && !environment.IsDevelopment()
                    ? "An unexpected error occurred. Please try again or contact support."
                    : exception.Message
            }
        });
    }
}
