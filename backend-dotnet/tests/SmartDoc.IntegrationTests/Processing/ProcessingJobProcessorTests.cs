using Amazon.S3;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task ProcessNextAsync_WithRealPdf_ParsesChunksEmbedsAndPersistsChunks()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);

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
                context, fileStorage, CreateAiServiceClient(), NullLogger<ProcessingJobProcessor>.Instance);

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
            context, new MinioFileStorage(CreateS3Client(), BucketName), CreateAiServiceClient(), NullLogger<ProcessingJobProcessor>.Instance);

        var processed = await processor.ProcessNextAsync();

        processed.Should().BeFalse();
    }
}
