namespace SmartDoc.Domain.Entities;

public class User
{
    public const int MaxEmailLength = 320;

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core
    private User()
    {
    }

    public User(Guid id, string email, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email cannot be empty.", nameof(email));
        }

        if (email.Length > MaxEmailLength)
        {
            throw new ArgumentException($"Email cannot exceed {MaxEmailLength} characters.", nameof(email));
        }

        Id = id;
        Email = email;
        CreatedAt = createdAt;
    }
}
