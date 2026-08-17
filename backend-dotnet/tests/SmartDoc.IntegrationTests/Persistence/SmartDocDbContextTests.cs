using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;

namespace SmartDoc.IntegrationTests.Persistence;

/// <summary>
/// Exercises SmartDocDbContext against the real Postgres instance from docker-compose.yml.
/// Each test cleans up the rows it creates.
/// </summary>
public class SmartDocDbContextTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public SmartDocDbContextTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveChanges_PersistsUserAndDocument_RoundTripsExpectedValues()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Documents.Add(document);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            await using var readContext = _fixture.CreateContext();

            var reloadedDocument = await readContext.Documents.SingleAsync(d => d.Id == document.Id);

            reloadedDocument.UserId.Should().Be(user.Id);
            reloadedDocument.FileName.Should().Be("report.pdf");
            reloadedDocument.ContentType.Should().Be("application/pdf");
            reloadedDocument.Status.Should().Be(DocumentStatus.Uploaded);
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Documents.Remove(document);
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_PersistsDocument_StoresStatusAsReadablePostgresText()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.Users.Add(user);
        context.Documents.Add(document);
        await context.SaveChangesAsync();

        try
        {
            // Reads the column directly, bypassing the enum conversion, to confirm the value
            // stored is the readable string "Uploaded" and not the numeric enum value.
            var rawStatus = await context.Database
                .SqlQuery<string>($"SELECT \"Status\" AS \"Value\" FROM \"Documents\" WHERE \"Id\" = {document.Id}")
                .SingleAsync();

            rawStatus.Should().Be("Uploaded");
        }
        finally
        {
            context.Documents.Remove(document);
            context.Users.Remove(user);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_DuplicateEmail_ThrowsDbUpdateException()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var user1 = new User(Guid.NewGuid(), email, "test-password-hash", DateTimeOffset.UtcNow);
        var user2 = new User(Guid.NewGuid(), email, "test-password-hash", DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.Users.Add(user1);
        await context.SaveChangesAsync();

        try
        {
            context.Users.Add(user2);
            var act = async () => await context.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            context.Entry(user2).State = EntityState.Detached;
            context.Users.Remove(user1);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_DocumentWithNonExistentUserId_ThrowsDbUpdateException()
    {
        var document = new Document(
            Guid.NewGuid(), Guid.NewGuid(), "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.Documents.Add(document);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
