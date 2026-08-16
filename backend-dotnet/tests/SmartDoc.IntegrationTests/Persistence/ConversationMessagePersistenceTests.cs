using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;

namespace SmartDoc.IntegrationTests.Persistence;

/// <summary>
/// Exercises Conversation/Message persistence against real Postgres: round-trip, the
/// Restrict FK Conversation -> User (same reasoning as Document -> User, ADR 0006) and the
/// Cascade FK Message -> Conversation (same reasoning as ProcessingJob -> Document).
/// </summary>
public class ConversationMessagePersistenceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ConversationMessagePersistenceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveChanges_PersistsConversationAndMessages_RoundTripsExpectedValues()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        var conversation = new Conversation(Guid.NewGuid(), user.Id, DateTimeOffset.UtcNow);
        var userMessage = new Message(Guid.NewGuid(), conversation.Id, MessageRole.User, "What is RAG?", DateTimeOffset.UtcNow);
        var assistantMessage = new Message(
            Guid.NewGuid(), conversation.Id, MessageRole.Assistant, "RAG is...\n\nSources:\nfoo.pdf — page 1", DateTimeOffset.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Conversations.Add(conversation);
            writeContext.Messages.AddRange(userMessage, assistantMessage);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            await using var readContext = _fixture.CreateContext();
            var reloadedConversation = await readContext.Conversations.SingleAsync(c => c.Id == conversation.Id);
            var reloadedMessages = await readContext.Messages
                .Where(m => m.ConversationId == conversation.Id)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            reloadedConversation.UserId.Should().Be(user.Id);
            reloadedMessages.Should().HaveCount(2);
            reloadedMessages[0].Role.Should().Be(MessageRole.User);
            reloadedMessages[1].Role.Should().Be(MessageRole.Assistant);
            reloadedMessages[1].Content.Should().Contain("Sources:");
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Conversations.Remove(conversation); // cascades the messages
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_ConversationWithNonExistentUserId_ThrowsDbUpdateException()
    {
        var conversation = new Conversation(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.Conversations.Add(conversation);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_MessageWithNonExistentConversationId_ThrowsDbUpdateException()
    {
        var message = new Message(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User, "text", DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.Messages.Add(message);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingConversation_CascadeDeletesItsMessages()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        var conversation = new Conversation(Guid.NewGuid(), user.Id, DateTimeOffset.UtcNow);
        var message = new Message(Guid.NewGuid(), conversation.Id, MessageRole.User, "text", DateTimeOffset.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Conversations.Add(conversation);
            writeContext.Messages.Add(message);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            await using (var deleteContext = _fixture.CreateContext())
            {
                var trackedConversation = await deleteContext.Conversations.SingleAsync(c => c.Id == conversation.Id);
                deleteContext.Conversations.Remove(trackedConversation);
                await deleteContext.SaveChangesAsync();
            }

            await using var verifyContext = _fixture.CreateContext();
            var messageStillExists = await verifyContext.Messages.AnyAsync(m => m.Id == message.Id);
            messageStillExists.Should().BeFalse();
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task DeletingUser_WithExistingConversation_ThrowsDbUpdateException()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        var conversation = new Conversation(Guid.NewGuid(), user.Id, DateTimeOffset.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Conversations.Add(conversation);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            // A fresh context with no knowledge of the dependent Conversation, so this
            // actually exercises Postgres's FK constraint rather than EF's client-side
            // cascade-severance detection (which throws InvalidOperationException earlier
            // if both entities are tracked in the same context).
            await using var deleteContext = _fixture.CreateContext();
            var trackedUser = await deleteContext.Users.SingleAsync(u => u.Id == user.Id);
            deleteContext.Users.Remove(trackedUser);

            var act = async () => await deleteContext.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Conversations.Remove(conversation);
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }
}
