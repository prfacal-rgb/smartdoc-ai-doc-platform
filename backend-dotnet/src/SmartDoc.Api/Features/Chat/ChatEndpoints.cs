using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/", PostChatAsync);
        group.MapGet("/{conversationId:guid}", GetConversationAsync);
    }

    private static async Task<Results<Ok<ChatResponse>, ValidationProblem, NotFound<ProblemDetails>>> PostChatAsync(
        ChatRequest request,
        IValidator<ChatRequest> validator,
        SmartDocDbContext db,
        IAiServiceClient aiServiceClient,
        SimilaritySearchService similaritySearchService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken);
        if (!userExists)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "User not found",
                Detail = $"No user exists with id '{request.UserId}'.",
            });
        }

        var now = DateTimeOffset.UtcNow;
        Conversation conversation;

        if (request.ConversationId is { } conversationId)
        {
            var existing = await db.Conversations
                .SingleOrDefaultAsync(c => c.Id == conversationId && c.UserId == request.UserId, cancellationToken);

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
            conversation = new Conversation(Guid.NewGuid(), request.UserId, now);
            db.Conversations.Add(conversation);
        }

        var topK = configuration.GetValue("Rag:DefaultTopK", 5);
        var maxRelevantDistance = configuration.GetValue("Rag:MaxRelevantDistance", 0.75);

        var embedResult = await aiServiceClient.EmbedAsync([request.Question], cancellationToken);
        var matches = await similaritySearchService.SearchAsync(embedResult.Embeddings[0], topK, cancellationToken);
        var relevantMatches = matches.Where(m => m.Distance <= maxRelevantDistance).ToList();

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
        Guid conversationId, SmartDocDbContext db, CancellationToken cancellationToken)
    {
        var conversationExists = await db.Conversations.AnyAsync(c => c.Id == conversationId, cancellationToken);
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
