using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Api;
using SmartDoc.Api.Features.Auth;
using SmartDoc.Application.AiService;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;
using SmartDoc.Infrastructure.Persistence;
using SmartDoc.Infrastructure.Search;

namespace SmartDoc.Api.Features.Chat;

public static class ChatEndpoints
{
    // PROJECT.md §7: "comportamiento 'I don't know / insufficient context'" — returned
    // directly, without ever calling /generate, when nothing retrieved clears the
    // similarity threshold (Rag:MaxRelevantDistance).
    private const string InsufficientContextAnswer =
        "I don't have enough information in the uploaded documents to answer this question.";

    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat").RequireAuthorization();

        group.MapPost("/", PostChatAsync);
        group.MapGet("/{conversationId:guid}", GetConversationAsync);
    }

    private static async Task<Results<Ok<ChatResponse>, ValidationProblem, NotFound<ProblemDetails>>> PostChatAsync(
        ChatRequest request,
        ClaimsPrincipal claimsPrincipal,
        IValidator<ChatRequest> validator,
        SmartDocDbContext db,
        IAiServiceClient aiServiceClient,
        SimilaritySearchService similaritySearchService,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // RagDistanceLog.CategoryName (not ILogger<ChatEndpoints> — static classes can't be
        // a generic type argument, CS0718) routes this to its own file via a Serilog
        // sub-logger (ADR 0020), on top of the general app log.
        var logger = loggerFactory.CreateLogger(RagDistanceLog.CategoryName);
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        // No "does this user exist" check anymore (ADR 0017) — same reasoning as
        // DocumentEndpoints: userId comes from a validated JWT now, not client input.
        var userId = claimsPrincipal.GetUserId();
        var now = DateTimeOffset.UtcNow;
        Conversation conversation;

        if (request.ConversationId is { } conversationId)
        {
            var existing = await db.Conversations
                .SingleOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, cancellationToken);

            if (existing is null)
            {
                // Same response whether the conversation doesn't exist or belongs to a
                // different user — doesn't reveal which.
                return TypedResults.NotFound(new ProblemDetails
                {
                    Title = "Conversation not found",
                    Detail = $"No conversation exists with id '{conversationId}' for this user.",
                });
            }

            conversation = existing;
        }
        else
        {
            conversation = new Conversation(Guid.NewGuid(), userId, now);
            db.Conversations.Add(conversation);
        }

        var topK = configuration.GetValue("Rag:DefaultTopK", 5);
        // 0.5 empirically calibrated (see ADR 0022, recalibrated for bge-m3 in ADR 0026) -
        // not a guess like the 0.75 it originally replaced.
        var maxRelevantDistance = configuration.GetValue("Rag:MaxRelevantDistance", 0.5);

        var embedResult = await aiServiceClient.EmbedAsync([request.Question], cancellationToken);
        var matches = await similaritySearchService.SearchAsync(embedResult.Embeddings[0], topK, cancellationToken);
        var relevantMatches = matches.Where(m => m.Distance <= maxRelevantDistance).ToList();

        // Raw distances, logged before the threshold filter above — the empirical signal
        // ADR 0016 flagged as needed to calibrate Rag:MaxRelevantDistance against real
        // traffic instead of guessing. {@Distances} is structured (Serilog, ADR 0020), so
        // this is queryable later without re-running anything.
        logger.LogInformation(
            "Similarity search returned {MatchCount} candidate(s), {RelevantCount} within threshold {Threshold}. Distances: {@Distances}",
            matches.Count, relevantMatches.Count, maxRelevantDistance, matches.Select(m => Math.Round(m.Distance, 4)));

        string answer;
        List<Citation> sources;

        if (relevantMatches.Count == 0)
        {
            answer = InsufficientContextAnswer;
            sources = [];
        }
        else
        {
            var contextTexts = relevantMatches.Select(m => m.Text).ToList();
            var generateResult = await aiServiceClient.GenerateAsync(request.Question, contextTexts, cancellationToken);

            answer = generateResult.Answer;
            // Multiple chunks can come from the same file/page — list each source once.
            sources = relevantMatches.Select(m => new Citation(m.FileName, m.PageNumber)).Distinct().ToList();
        }

        // Full formatted answer (prose + "Sources:") is what gets persisted (ADR 0015); the
        // API response keeps them structured separately for the caller.
        var fullContent = sources.Count == 0
            ? answer
            : $"{answer}\n\nSources:\n{string.Join('\n', sources.Select(s => $"{s.FileName} — page {s.PageNumber}"))}";

        db.Messages.Add(new Message(Guid.NewGuid(), conversation.Id, MessageRole.User, request.Question, now));
        db.Messages.Add(new Message(Guid.NewGuid(), conversation.Id, MessageRole.Assistant, fullContent, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ChatResponse(conversation.Id, answer, sources));
    }

    private static async Task<Results<Ok<ConversationHistoryResponse>, NotFound>> GetConversationAsync(
        Guid conversationId, ClaimsPrincipal claimsPrincipal, SmartDocDbContext db, CancellationToken cancellationToken)
    {
        // Unlike Documents (shared knowledge base), a conversation is personal — only its
        // owner can read it. Same 404-hides-existence pattern as PostChatAsync above.
        var userId = claimsPrincipal.GetUserId();
        var conversationExists = await db.Conversations
            .AnyAsync(c => c.Id == conversationId && c.UserId == userId, cancellationToken);

        if (!conversationExists)
        {
            return TypedResults.NotFound();
        }

        var messages = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ConversationHistoryResponse(conversationId, messages.Select(MessageResponse.FromEntity).ToList()));
    }
}
