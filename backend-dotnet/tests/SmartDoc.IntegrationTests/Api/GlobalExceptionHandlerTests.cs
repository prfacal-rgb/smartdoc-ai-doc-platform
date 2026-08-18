using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SmartDoc.Api;

namespace SmartDoc.IntegrationTests.Api;

/// <summary>
/// Unit-level coverage of GlobalExceptionHandler.MapException (ADR 0020) — the exception ->
/// status/title/detail decision, tested directly without a host or a real
/// IProblemDetailsService, same "extract the decision so it's testable without the
/// machinery" pattern already used for ProcessingJobProcessor. End-to-end pipeline wiring
/// (UseExceptionHandler actually catching a real exception and producing a ProblemDetails
/// body) is covered separately in ChatEndpointsTests.
/// </summary>
public class GlobalExceptionHandlerTests
{
    [Fact]
    public void MapException_WithHttpRequestException_ReturnsServiceUnavailable()
    {
        var (statusCode, title, detail) = GlobalExceptionHandler.MapException(new HttpRequestException("connection refused"));

        statusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        title.Should().Be("AI service unavailable");
        detail.Should().NotContain("connection refused"); // internal detail stays out of the response
    }

    [Fact]
    public void MapException_WithUnexpectedException_ReturnsInternalServerErrorWithoutLeakingMessage()
    {
        var (statusCode, title, detail) = GlobalExceptionHandler.MapException(
            new InvalidOperationException("Host=db;Password=super-secret"));

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        title.Should().Be("An unexpected error occurred");
        detail.Should().NotContain("super-secret");
    }
}
