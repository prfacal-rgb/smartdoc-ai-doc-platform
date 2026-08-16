using FluentAssertions;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;

namespace SmartDoc.UnitTests.Domain;

public class MessageTests
{
    [Fact]
    public void Constructor_WithValidData_SetsProperties()
    {
        var id = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var message = new Message(id, conversationId, MessageRole.User, "What is RAG?", createdAt);

        message.Id.Should().Be(id);
        message.ConversationId.Should().Be(conversationId);
        message.Role.Should().Be(MessageRole.User);
        message.Content.Should().Be("What is RAG?");
        message.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        var act = () => new Message(Guid.Empty, Guid.NewGuid(), MessageRole.User, "text", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public void Constructor_WithEmptyConversationId_Throws()
    {
        var act = () => new Message(Guid.NewGuid(), Guid.Empty, MessageRole.User, "text", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("conversationId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyContent_Throws(string content)
    {
        var act = () => new Message(Guid.NewGuid(), Guid.NewGuid(), MessageRole.Assistant, content, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("content");
    }
}
