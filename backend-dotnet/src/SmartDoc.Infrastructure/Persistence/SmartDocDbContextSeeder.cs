using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Infrastructure.Persistence;

/// <summary>
/// Ensures the single seed user (see CLAUDE.md — no public registration in the MVP) exists,
/// with its password hashed via PasswordHasher&lt;User&gt; (PBKDF2 — same algorithm ASP.NET Core
/// Identity uses internally). Idempotent — safe to run on every startup. If the user already
/// exists but the configured password changed since, the stored hash is resynced, so this is
/// a live sync, not a one-time bootstrap.
/// </summary>
public static class SmartDocDbContextSeeder
{
    public static async Task SeedAsync(
        SmartDocDbContext context, string seedUserEmail, string seedUserPassword, CancellationToken cancellationToken = default)
    {
        var hasher = new PasswordHasher<User>();
        var existingUser = await context.Users.SingleOrDefaultAsync(u => u.Email == seedUserEmail, cancellationToken);

        if (existingUser is null)
        {
            // PasswordHasher.HashPassword ignores the `user` argument in the default
            // implementation (it exists for API symmetry with VerifyHashedPassword, not
            // because the hash depends on any User property) — safe to pass null here,
            // before the User itself exists yet.
            var passwordHash = hasher.HashPassword(null!, seedUserPassword);
            var seedUser = new User(Guid.NewGuid(), seedUserEmail, passwordHash, DateTimeOffset.UtcNow);
            context.Users.Add(seedUser);
        }
        else
        {
            existingUser.SetPasswordHash(hasher.HashPassword(existingUser, seedUserPassword));
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
