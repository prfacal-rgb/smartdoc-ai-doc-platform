using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;

namespace SmartDoc.IntegrationTests.Persistence;

/// <summary>
/// Exercises ProcessingJob persistence against the real Postgres instance, including the FK
/// to Document (cascade delete — see ProcessingJobConfiguration, contrast with the
/// Document -> User Restrict FK).
/// </summary>
public class ProcessingJobPersistenceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ProcessingJobPersistenceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveChanges_PersistsProcessingJob_RoundTripsExpectedValues()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);
        var job = new ProcessingJob(Guid.NewGuid(), document.Id, DateTimeOffset.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Documents.Add(document);
            writeContext.ProcessingJobs.Add(job);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            await using var readContext = _fixture.CreateContext();
            var reloaded = await readContext.ProcessingJobs.SingleAsync(j => j.Id == job.Id);

            reloaded.DocumentId.Should().Be(document.Id);
            reloaded.Status.Should().Be(ProcessingJobStatus.Pending);
            reloaded.RetryCount.Should().Be(0);
            reloaded.ErrorMessage.Should().BeNull();
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Documents.Remove(document); // cascades the ProcessingJob
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_ProcessingJobWithNonExistentDocumentId_ThrowsDbUpdateException()
    {
        var job = new ProcessingJob(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.ProcessingJobs.Add(job);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingDocument_CascadeDeletesItsProcessingJobs()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);
        var job = new ProcessingJob(Guid.NewGuid(), document.Id, DateTimeOffset.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Documents.Add(document);
            writeContext.ProcessingJobs.Add(job);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            await using (var deleteContext = _fixture.CreateContext())
            {
                var trackedDocument = await deleteContext.Documents.SingleAsync(d => d.Id == document.Id);
                deleteContext.Documents.Remove(trackedDocument);
                await deleteContext.SaveChangesAsync();
            }

            await using var verifyContext = _fixture.CreateContext();
            var jobStillExists = await verifyContext.ProcessingJobs.AnyAsync(j => j.Id == job.Id);
            jobStillExists.Should().BeFalse();
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }
}
