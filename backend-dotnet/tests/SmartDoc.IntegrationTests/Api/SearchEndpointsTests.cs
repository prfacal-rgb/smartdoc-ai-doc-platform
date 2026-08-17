using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartDoc.Api.Features.Search;
using SmartDoc.Application.AiService;
using SmartDoc.Domain.Entities;
using SmartDoc.IntegrationTests.Persistence;

namespace SmartDoc.IntegrationTests.Api;

/// <summary>
/// End-to-end tests for POST /api/search against the real stack (Postgres/pgvector,
/// ai-service, Ollama). The seeded chunk's embedding comes from a real /embed call, not a
/// synthetic vector — SimilaritySearchServiceTests already validates the raw SQL query in
/// isolation with synthetic vectors; this validates the actual retrieval quality end-to-end.
/// </summary>
public class SearchEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly DatabaseFixture _dbFixture = new();
    private HttpClient _client = null!;
    private User _testUser = null!;

    public SearchEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();

        _testUser = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), _testUser.Id, "sunset.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);

        var aiServiceClient = _factory.Services.GetRequiredService<IAiServiceClient>();
        var embedResult = await aiServiceClient.EmbedAsync(["The sky described in this document is bright orange at sunset."]);

        var chunk = new DocumentChunk(
            Guid.NewGuid(), document.Id, 0, 3, "The sky described in this document is bright orange at sunset.",
            embedResult.Model, embedResult.Embeddings[0], DateTimeOffset.UtcNow);

        await using var db = _dbFixture.CreateContext();
        db.Users.Add(_testUser);
        db.Documents.Add(document);
        db.DocumentChunks.Add(chunk);
        await db.SaveChangesAsync();

        _client.AuthenticateAs(_factory, _testUser);
    }

    public async Task DisposeAsync()
    {
        await using var db = _dbFixture.CreateContext();
        db.Documents.RemoveRange(db.Documents.Where(d => d.UserId == _testUser.Id)); // cascades the chunk
        await db.SaveChangesAsync();
        db.Users.Remove(_testUser);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PostSearch_WithoutToken_ReturnsUnauthorized()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync("/api/search", new SearchRequest("anything", null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostSearch_WithRelatedQuery_ReturnsMatchingChunk()
    {
        var response = await _client.PostAsJsonAsync("/api/search", new SearchRequest("What color is the sky at sunset?", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>();
        body!.Results.Should().NotBeEmpty();
        body.Results[0].FileName.Should().Be("sunset.pdf");
        body.Results[0].PageNumber.Should().Be(3);
    }

    [Fact]
    public async Task PostSearch_WithEmptyQuery_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/search", new SearchRequest("", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostSearch_WithTopKZero_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/search", new SearchRequest("anything", 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
