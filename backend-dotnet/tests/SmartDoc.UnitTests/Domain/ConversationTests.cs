using FluentAssertions;
using SmartDoc.Domain.Entities;

namespace SmartDoc.UnitTests.Domain;

public class ConversationTests
{
    [Fact]
    public void Constructor_WithValidData_SetsProperties()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var conversation = new Conversation(id, userId, createdAt);

        conversation.Id.Should().Be(id);
        conversation.UserId.Should().Be(userId);
        conversation.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        var act = () => new Conversation(Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public void Constructor_WithEmptyUserId_Throws()
    {
        var act = () => new Conversation(Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("userId");
    }
}
