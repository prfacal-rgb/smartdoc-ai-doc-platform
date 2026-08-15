using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;
using SmartDoc.Infrastructure.Processing;
using SmartDoc.IntegrationTests.Persistence;

namespace SmartDoc.IntegrationTests.Processing;

/// <summary>
/// Exercises ProcessingJobProcessor directly against a real SmartDocDbContext, without the
/// BackgroundService polling loop around it (see ProcessingJobProcessor for why it's
/// extracted).
/// </summary>
public class ProcessingJobProcessorTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ProcessingJobProcessorTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessNextAsync_WithPendingJob_TransitionsJobToDoneAndDocumentToReady()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);
        var job = new ProcessingJob(Guid.NewGuid(), document.Id, DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.Users.Add(user);
        context.Documents.Add(document);
        context.ProcessingJobs.Add(job);
        await context.SaveChangesAsync();

        try
        {
            var processor = new ProcessingJobProcessor(context, NullLogger<ProcessingJobProcessor>.Instance);

            var processed = await processor.ProcessNextAsync();

            processed.Should().BeTrue();

            var reloadedJob = await context.ProcessingJobs.SingleAsync(j => j.Id == job.Id);
            var reloadedDocument = await context.Documents.SingleAsync(d => d.Id == document.Id);

            reloadedJob.Status.Should().Be(ProcessingJobStatus.Done);
            reloadedDocument.Status.Should().Be(DocumentStatus.Ready);
        }
        finally
        {
            context.Documents.Remove(document); // cascades the ProcessingJob
            context.Users.Remove(user);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ProcessNextAsync_WithNoPendingJobs_ReturnsFalse()
    {
        await using var context = _fixture.CreateContext();
        var processor = new ProcessingJobProcessor(context, NullLogger<ProcessingJobProcessor>.Instance);

        var processed = await processor.ProcessNextAsync();

        processed.Should().BeFalse();
    }
}
