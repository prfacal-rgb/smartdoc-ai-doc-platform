using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Infrastructure.Persistence;

/// <summary>
/// Ensures the single seed user (see CLAUDE.md — no public registration in the MVP) exists.
/// This is not authentication: it only guarantees a valid User row is available so that
/// Document endpoints have a real UserId to reference. Idempotent — safe to run on every
/// startup.
/// </summary>
public static class SmartDocDbContextSeeder
{
    public static async Task SeedAsync(SmartDocDbContext context, string seedUserEmail, CancellationToken cancellationToken = default)
    {
        var alreadySeeded = await context.Users.AnyAsync(u => u.Email == seedUserEmail, cancellationToken);
        if (alreadySeeded)
        {
            return;
        }

        var seedUser = new User(Guid.NewGuid(), seedUserEmail, DateTimeOffset.UtcNow);
        context.Users.Add(seedUser);
        await context.SaveChangesAsync(cancellationToken);
    }
}
