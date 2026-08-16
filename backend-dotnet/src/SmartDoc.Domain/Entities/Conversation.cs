namespace SmartDoc.Domain.Entities;

public class Conversation
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core
    private Conversation()
    {
    }

    public Conversation(Guid id, Guid userId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id cannot be empty.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        Id = id;
        UserId = userId;
        CreatedAt = createdAt;
    }
}
