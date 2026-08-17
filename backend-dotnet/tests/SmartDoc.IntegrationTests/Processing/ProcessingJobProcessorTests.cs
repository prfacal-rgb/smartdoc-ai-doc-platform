using Amazon.S3;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;
using SmartDoc.Infrastructure.AiService;
using SmartDoc.Infrastructure.Processing;
using SmartDoc.Infrastructure.Storage;
using SmartDoc.IntegrationTests.Persistence;

namespace SmartDoc.IntegrationTests.Processing;

/// <summary>
/// Exercises ProcessingJobProcessor end-to-end against the real stack: Postgres, MinIO, and
/// ai-service-python (which in turn calls the real Ollama instance on the physical host).
/// Requires `docker compose up -d` (postgres, minio, ai-service) to be running.
/// </summary>
public class ProcessingJobProcessorTests : IClassFixture<DatabaseFixture>
{
    private const string BucketName = "smartdoc-documents";

    private readonly DatabaseFixture _fixture;

    public ProcessingJobProcessorTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static IAmazonS3 CreateS3Client() => new AmazonS3Client(
        "smartdoc",
        "smartdoc_dev_password",
        new AmazonS3Config { ServiceURL = "http://localhost:9000", ForcePathStyle = true });

    private static AiServiceClient CreateAiServiceClient() =>
        new(new HttpClient { BaseAddress = new Uri("http://localhost:8000"), Timeout = TimeSpan.FromSeconds(60) });

    private static IConfiguration CreateConfiguration(int maxRetries = 3) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Worker:MaxRetries"] = maxRetries.ToString() })
        .Build();

    [Fact]
    public async Task ProcessNextAsync_WithRealPdf_ParsesChunksEmbedsAndPersistsChunks()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);

        var fileStorage = new MinioFileStorage(CreateS3Client(), BucketName);
        var pdfBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf"));

        string storagePath;
        await using (var stream = new MemoryStream(pdfBytes))
        {
            storagePath = await fileStorage.SaveAsync(stream, "sample.pdf", "application/pdf");
        }

        var document = new Document(Guid.NewGuid(), user.Id, "sample.pdf", "application/pdf", storagePath, DateTimeOffset.UtcNow);
        var job = new ProcessingJob(Guid.NewGuid(), document.Id, DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.Users.Add(user);
        context.Documents.Add(document);
        context.ProcessingJobs.Add(job);
        await context.SaveChangesAsync();

        try
        {
            var processor = new ProcessingJobProcessor(
                context, fileStorage, CreateAiServiceClient(), CreateConfiguration(), NullLogger<ProcessingJobProcessor>.Instance);

            var processed = await processor.ProcessNextAsync();

            processed.Should().BeTrue();

            var reloadedJob = await context.ProcessingJobs.SingleAsync(j => j.Id == job.Id);
            var reloadedDocument = await context.Documents.SingleAsync(d => d.Id == document.Id);
            var chunks = await context.DocumentChunks.Where(c => c.DocumentId == document.Id).ToListAsync();

            reloadedJob.Status.Should().Be(ProcessingJobStatus.Done);
            reloadedDocument.Status.Should().Be(DocumentStatus.Ready);

            chunks.Should().NotBeEmpty();
            chunks.Should().OnlyContain(c => c.EmbeddingModel == "nomic-embed-text");
            chunks.Should().OnlyContain(c => c.Embedding.Length == DocumentChunk.EmbeddingDimensions);
            chunks.Should().OnlyContain(c => c.PageNumber == 1); // the fixture is a single-page PDF
            chunks.Select(c => c.ChunkIndex).Order().Should().BeEquivalentTo(Enumerable.Range(0, chunks.Count));
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Documents.RemoveRange(cleanupContext.Documents.Where(d => d.UserId == user.Id));
            await cleanupContext.SaveChangesAsync();
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();

            await fileStorage.DeleteAsync(storagePath);
        }
    }

    [Fact]
    public async Task ProcessNextAsync_WithNoPendingJobs_ReturnsFalse()
    {
        await using var context = _fixture.CreateContext();
        var processor = new ProcessingJobProcessor(
            context, new MinioFileStorage(CreateS3Client(), BucketName), CreateAiServiceClient(), CreateConfiguration(),
            NullLogger<ProcessingJobProcessor>.Instance);

        var processed = await processor.ProcessNextAsync();

        processed.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessNextAsync_WhenFileStorageKeepsFailing_RetriesUpToMaxRetriesThenFailsPermanently()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);

        // StoragePath was never uploaded to MinIO — fileStorage.GetAsync will keep throwing a
        // real AmazonS3Exception ("key not found"), a genuine (if contrived) transient-looking
        // failure from the real service, no mocking needed.
        var document = new Document(
            Guid.NewGuid(), user.Id, "missing.pdf", "application/pdf", $"/nonexistent/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);
        var job = new ProcessingJob(Guid.NewGuid(), document.Id, DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.Users.Add(user);
        context.Documents.Add(document);
        context.ProcessingJobs.Add(job);
        await context.SaveChangesAsync();

        try
        {
            var processor = new ProcessingJobProcessor(
                context, new MinioFileStorage(CreateS3Client(), BucketName), CreateAiServiceClient(), CreateConfiguration(maxRetries: 2),
                NullLogger<ProcessingJobProcessor>.Instance);

            // Attempt 1: fails, 1 <= 2 retries allowed -> back to Pending, Document stays Processing.
            await processor.ProcessNextAsync();
            var afterFirstFailure = await context.ProcessingJobs.SingleAsync(j => j.Id == job.Id);
            var documentAfterFirstFailure = await context.Documents.SingleAsync(d => d.Id == document.Id);
            afterFirstFailure.Status.Should().Be(ProcessingJobStatus.Pending);
            afterFirstFailure.RetryCount.Should().Be(1);
            documentAfterFirstFailure.Status.Should().Be(DocumentStatus.Processing);

            // Attempt 2: fails again, 2 <= 2 -> still Pending.
            await processor.ProcessNextAsync();
            var afterSecondFailure = await context.ProcessingJobs.SingleAsync(j => j.Id == job.Id);
            afterSecondFailure.Status.Should().Be(ProcessingJobStatus.Pending);
            afterSecondFailure.RetryCount.Should().Be(2);

            // Attempt 3: fails a third time, 3 <= 2 is false -> permanently Failed, Document too.
            await processor.ProcessNextAsync();
            var afterThirdFailure = await context.ProcessingJobs.SingleAsync(j => j.Id == job.Id);
            var documentAfterThirdFailure = await context.Documents.SingleAsync(d => d.Id == document.Id);
            afterThirdFailure.Status.Should().Be(ProcessingJobStatus.Failed);
            afterThirdFailure.RetryCount.Should().Be(3);
            afterThirdFailure.ErrorMessage.Should().NotBeNullOrWhiteSpace();
            documentAfterThirdFailure.Status.Should().Be(DocumentStatus.Failed);

            // Exhausted -> no longer picked up by later polls.
            var pickedUpAgain = await processor.ProcessNextAsync();
            pickedUpAgain.Should().BeFalse();
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Documents.RemoveRange(cleanupContext.Documents.Where(d => d.UserId == user.Id));
            await cleanupContext.SaveChangesAsync();
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }
}
