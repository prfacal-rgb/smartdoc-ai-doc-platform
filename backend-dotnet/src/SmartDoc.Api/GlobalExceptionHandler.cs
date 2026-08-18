using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SmartDoc.Api;

/// <summary>
/// Catch-all for exceptions that escape endpoint handlers (ADR 0020) — until now those
/// bubbled as bare unhandled exceptions (dev exception page in Development, an empty 500 in
/// Production), with no consistent logging and no ProblemDetails body for API clients to
/// parse. Registered via AddExceptionHandler + UseExceptionHandler, ASP.NET Core's built-in
/// hook for exactly this ("IExceptionHandler" pattern, .NET 8+) rather than a hand-rolled
/// middleware.
///
/// The one external dependency call this Api makes synchronously in the request path is
/// AiServiceClient (ai-service down/erroring surfaces as HttpRequestException via
/// EnsureSuccessStatusCode) — mapped to 503 explicitly since "the AI service is unreachable"
/// is a realistic, distinguishable failure mode a caller should be able to tell apart from
/// "something is broken in the Api itself". Everything else maps to a generic 500 — expanding
/// that mapping further without a second real failure mode to justify it would be guessing.
/// </summary>
public class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = MapException(exception);

        logger.LogError(
            exception, "Unhandled exception on {Method} {Path} mapped to {StatusCode} ({Title}).",
            httpContext.Request.Method, httpContext.Request.Path, statusCode, title);

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
            },
        });
    }

    // Public/static so it can be unit-tested directly, without spinning up a host or a real
    // IProblemDetailsService.
    public static (int StatusCode, string Title, string Detail) MapException(Exception exception) => exception switch
    {
        HttpRequestException => (
            StatusCodes.Status503ServiceUnavailable,
            "AI service unavailable",
            "The AI service is temporarily unreachable. Please try again shortly."),
        // Deliberately not exception.Message here: it can leak internal detail (connection
        // strings, file paths, provider errors). The real message goes to the log above, not
        // to the response body.
        _ => (
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred",
            "An unexpected error occurred while processing the request."),
    };
}
