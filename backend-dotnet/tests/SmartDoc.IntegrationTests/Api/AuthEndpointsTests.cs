using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartDoc.Api.Features.Auth;

namespace SmartDoc.IntegrationTests.Api;

/// <summary>
/// End-to-end tests for POST /api/auth/login against the real seed user (created by
/// SmartDocDbContextSeeder on startup — see Program.cs).
/// </summary>
public class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (string Email, string Password) GetSeedCredentials()
    {
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        var email = configuration["Jwt:SeedUserEmail"]!;
        var password = configuration["Jwt:SeedUserPassword"]!;
        return (email, password);
    }

    [Fact]
    public async Task Login_WithValidSeedUserCredentials_ReturnsTokenAndFutureExpiration()
    {
        var (email, password) = GetSeedCredentials();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Token.Should().NotBeNullOrWhiteSpace();
        body.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var (email, _) = GetSeedCredentials();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "definitely-wrong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonExistentEmail_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest($"nobody-{Guid.NewGuid():N}@example.com", "anything"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithEmptyEmail_ReturnsValidationProblem()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("", "anything"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithEmptyPassword_ReturnsValidationProblem()
    {
        var (email, _) = GetSeedCredentials();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
