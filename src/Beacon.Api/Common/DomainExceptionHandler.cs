using Beacon.Application.Incidents;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Common;

/// <summary>
/// Translates domain/application exceptions into ProblemDetails responses instead of leaking a
/// raw 500 + stack trace for expected failure modes. Anything not matched here falls through to
/// the default developer-exception-page/500 behavior — this handler only owns the cases with a
/// clear, correct HTTP status.
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            IncidentNotFoundException => (StatusCodes.Status404NotFound, "Incident not found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Incident state conflict"),
            _ => (0, string.Empty)
        };

        if (statusCode == 0)
            return false;

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = exception.Message
            }
        });
    }
}
