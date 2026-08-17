namespace SmartDoc.Domain.Entities;

public class User
{
    public const int MaxEmailLength = 320;

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;

    /// <summary>
    /// Hashed with Microsoft.AspNetCore.Identity's PasswordHasher&lt;User&gt; (PBKDF2), never
    /// the plain-text password. See ADR 0017 — this project has a single seed user (no
    /// public registration), but the password is still hashed like a real system would.
    /// </summary>
    public string PasswordHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core
    private User()
    {
    }

    public User(Guid id, string email, string passwordHash, DateTimeOffset createdAt)
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

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("PasswordHash cannot be empty.", nameof(passwordHash));
        }

        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    /// <summary>Used by the seeder to keep the stored hash in sync if the configured seed
    /// password changes between restarts.</summary>
    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("PasswordHash cannot be empty.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }
}
