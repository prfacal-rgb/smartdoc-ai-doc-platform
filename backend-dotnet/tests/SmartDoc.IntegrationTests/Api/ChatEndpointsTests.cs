using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartDoc.Api.Features.Chat;
using SmartDoc.Application.AiService;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;
using SmartDoc.IntegrationTests.Persistence;

namespace SmartDoc.IntegrationTests.Api;

/// <summary>
/// End-to-end tests for POST /api/chat and GET /api/chat/{id} against the real stack
/// (Postgres, ai-service/Ollama for embeddings, ai-service/Groq for generation), now behind
/// auth (ADR 0017) — UserId comes from the bearer token's claims, not the request body.
/// </summary>
public class ChatEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly DatabaseFixture _dbFixture = new();
    private HttpClient _client = null!;
    private User _testUser = null!;

    public ChatEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _testUser = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);

        await using var db = _dbFixture.CreateContext();
        db.Users.Add(_testUser);
        await db.SaveChangesAsync();

        _client.AuthenticateAs(_factory, _testUser);
    }

    public async Task DisposeAsync()
    {
        await using var db = _dbFixture.CreateContext();
        var conversations = await db.Conversations.Where(c => c.UserId == _testUser.Id).ToListAsync();
        db.Conversations.RemoveRange(conversations); // cascades messages
        db.Documents.RemoveRange(db.Documents.Where(d => d.UserId == _testUser.Id)); // cascades chunks
        await db.SaveChangesAsync();
        db.Users.Remove(_testUser);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PostChat_WithoutToken_ReturnsUnauthorized()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync("/api/chat", new ChatRequest("hello", null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostChat_WithNoRelevantDocuments_ReturnsInsufficientContextAndNoSources()
    {
        var response = await _client.PostAsJsonAsync("/api/chat", new ChatRequest("What is the meaning of life?", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>();
        body!.Sources.Should().BeEmpty();
        body.Answer.Should().Contain("don't have enough information");
        body.ConversationId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PostChat_WithRelevantDocument_ReturnsGroundedAnswerWithCitationAndPersistsMessages()
    {
        var document = new Document(
            Guid.NewGuid(), _testUser.Id, "sunset.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);

        var aiServiceClient = _factory.Services.GetRequiredService<IAiServiceClient>();
        var embedResult = await aiServiceClient.EmbedAsync(["The sky described in this document is bright orange at sunset."]);

        var chunk = new DocumentChunk(
            Guid.NewGuid(), document.Id, 0, 3, "The sky described in this document is bright orange at sunset.",
            embedResult.Model, embedResult.Embeddings[0], DateTimeOffset.UtcNow);

        await using (var db = _dbFixture.CreateContext())
        {
            db.Documents.Add(document);
            db.DocumentChunks.Add(chunk);
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/chat", new ChatRequest("What color is the sky at sunset in this document?", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>();
        body!.Answer.ToLowerInvariant().Should().Contain("orange");
        body.Sources.Should().ContainSingle(s => s.FileName == "sunset.pdf" && s.PageNumber == 3);

        await using var verifyDb = _dbFixture.CreateContext();
        var messages = await verifyDb.Messages
            .Where(m => m.ConversationId == body.ConversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be(MessageRole.User);
        messages[1].Role.Should().Be(MessageRole.Assistant);
        messages[1].Content.Should().Contain("Sources:");
    }

    [Fact]
    public async Task PostChat_WithNonExistentConversationId_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/chat", new ChatRequest("hello", Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostChat_WhenAiServiceIsUnreachable_ReturnsServiceUnavailableProblemDetails()
    {
        // Real end-to-end pipeline test for GlobalExceptionHandler (ADR 0020): swaps
        // IAiServiceClient for a double that throws HttpRequestException — the same exception
        // AiServiceClient's EnsureSuccessStatusCode throws when ai-service/Groq/Ollama is
        // actually unreachable — so the exception has to flow through the real middleware
        // pipeline (ConfigureServices re-registration wins over Program.cs's own
        // AddHttpClient<IAiServiceClient, AiServiceClient>() — last registration resolves).
        using var brokenFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddScoped<IAiServiceClient, UnreachableAiServiceClient>()));

        using var client = brokenFactory.CreateClient();
        client.AuthenticateAs(brokenFactory, _testUser);

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest("hello", null));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be("AI service unavailable");
    }

    /// <summary>Test double standing in for a genuinely unreachable ai-service (ADR 0020).</summary>
    private sealed class UnreachableAiServiceClient : IAiServiceClient
    {
        public Task<IReadOnlyList<ParsedPage>> ParseAsync(
            Stream fileContent, string fileName, string contentType, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("ai-service unreachable (test double)");

        public Task<IReadOnlyList<TextChunk>> ChunkAsync(
            IReadOnlyList<ParsedPage> pages, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("ai-service unreachable (test double)");

        public Task<EmbedResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("ai-service unreachable (test double)");

        public Task<GenerateResult> GenerateAsync(
            string question, IReadOnlyList<string> contextChunks, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("ai-service unreachable (test double)");
    }

    [Fact]
    public async Task PostChat_WithEmptyQuestion_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/chat", new ChatRequest("", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostChat_WithSameConversationIdTwice_AppendsToSameConversation()
    {
        var firstResponse = await _client.PostAsJsonAsync("/api/chat", new ChatRequest("hello", null));
        var first = await firstResponse.Content.ReadFromJsonAsync<ChatResponse>();

        var secondResponse = await _client.PostAsJsonAsync(
            "/api/chat", new ChatRequest("how are you?", first!.ConversationId));
        var second = await secondResponse.Content.ReadFromJsonAsync<ChatResponse>();

        second!.ConversationId.Should().Be(first.ConversationId);

        await using var db = _dbFixture.CreateContext();
        var messageCount = await db.Messages.CountAsync(m => m.ConversationId == first.ConversationId);
        messageCount.Should().Be(4);
    }

    [Fact]
    public async Task GetConversation_AfterChat_ReturnsMessageHistory()
    {
        var chatResponse = await _client.PostAsJsonAsync("/api/chat", new ChatRequest("hello there", null));
        var chat = await chatResponse.Content.ReadFromJsonAsync<ChatResponse>();

        var response = await _client.GetAsync($"/api/chat/{chat!.ConversationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ConversationHistoryResponse>();
        body!.Messages.Should().HaveCount(2);
        body.Messages[0].Role.Should().Be("User");
        body.Messages[0].Content.Should().Be("hello there");
    }

    [Fact]
    public async Task GetConversation_WithNonExistentId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/chat/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetConversation_BelongingToAnotherUser_ReturnsNotFound()
    {
        var otherUser = new User(Guid.NewGuid(), $"other-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);

        await using (var db = _dbFixture.CreateContext())
        {
            db.Users.Add(otherUser);
            await db.SaveChangesAsync();
        }

        try
        {
            using var otherClient = _factory.CreateClient();
            otherClient.AuthenticateAs(_factory, otherUser);

            var chatResponse = await otherClient.PostAsJsonAsync("/api/chat", new ChatRequest("hello from another user", null));
            var chat = await chatResponse.Content.ReadFromJsonAsync<ChatResponse>();

            // _client is authenticated as _testUser, not otherUser — should not see it.
            var response = await _client.GetAsync($"/api/chat/{chat!.ConversationId}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await using var cleanupDb = _dbFixture.CreateContext();
            var conversations = await cleanupDb.Conversations.Where(c => c.UserId == otherUser.Id).ToListAsync();
            cleanupDb.Conversations.RemoveRange(conversations);
            await cleanupDb.SaveChangesAsync();
            cleanupDb.Users.Remove(otherUser);
            await cleanupDb.SaveChangesAsync();
        }
    }
}
