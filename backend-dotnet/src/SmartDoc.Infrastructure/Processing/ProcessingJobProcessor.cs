using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartDoc.Application.AiService;
using SmartDoc.Application.Storage;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;
using SmartDoc.Infrastructure.Persistence;

namespace SmartDoc.Infrastructure.Processing;

/// <summary>
/// Picks up and executes a single pending ProcessingJob. Extracted from the BackgroundService
/// polling loop so it can be exercised directly in tests without needing the hosted-service
/// machinery.
///
/// Phase 3: reads the file from object storage, calls the AI service (parse -> chunk ->
/// embed), and persists the resulting DocumentChunks. Any failure at any step fails the job
/// and the document as a whole — no partial-success/granular retry yet (ADR 0013).
/// </summary>
public class ProcessingJobProcessor(
    SmartDocDbContext db,
    IFileStorage fileStorage,
    IAiServiceClient aiServiceClient,
    ILogger<ProcessingJobProcessor> logger)
{
    /// <returns>true if a job was picked up and processed, false if none was pending.</returns>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var job = await db.ProcessingJobs
            .Where(j => j.Status == ProcessingJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        var document = await db.Documents.FindAsync([job.DocumentId], cancellationToken);
        if (document is null)
        {
            // Should be unreachable given the Cascade FK (ProcessingJob -> Document, ADR
            // 0009) — guarded rather than silently swallowed.
            job.MarkAsFailed($"Document {job.DocumentId} not found.", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError("Processing job {JobId} references a missing document {DocumentId}.", job.Id, job.DocumentId);
            return true;
        }

        job.MarkAsRunning(DateTimeOffset.UtcNow);
        document.MarkAsProcessing();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Processing job {JobId} for document {DocumentId} started.", job.Id, job.DocumentId);

        try
        {
            await using var fileStream = await fileStorage.GetAsync(document.StoragePath, cancellationToken);
            var pages = await aiServiceClient.ParseAsync(fileStream, document.FileName, document.ContentType, cancellationToken);

            var chunks = await aiServiceClient.ChunkAsync(pages, cancellationToken);
            if (chunks.Count == 0)
            {
                throw new InvalidOperationException(
                    "No chunks were produced from this document (empty or unreadable content).");
            }

            var embedResult = await aiServiceClient.EmbedAsync(chunks.Select(c => c.Text).ToList(), cancellationToken);
            if (embedResult.Embeddings.Count != chunks.Count)
            {
                throw new InvalidOperationException(
                    $"Embedding count ({embedResult.Embeddings.Count}) does not match chunk count ({chunks.Count}).");
            }

            var now = DateTimeOffset.UtcNow;
            var documentChunks = chunks.Zip(
                embedResult.Embeddings,
                (chunk, embedding) => new DocumentChunk(
                    Guid.NewGuid(), document.Id, chunk.ChunkIndex, chunk.PageNumber, chunk.Text, embedResult.Model, embedding, now));

            db.DocumentChunks.AddRange(documentChunks);

            job.MarkAsDone(DateTimeOffset.UtcNow);
            document.MarkAsReady();

            logger.LogInformation(
                "Processing job {JobId} for document {DocumentId} completed with {ChunkCount} chunks.",
                job.Id, job.DocumentId, chunks.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.MarkAsFailed(ex.Message, DateTimeOffset.UtcNow);
            document.MarkAsFailed();

            logger.LogError(ex, "Processing job {JobId} for document {DocumentId} failed.", job.Id, job.DocumentId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
