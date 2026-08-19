using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
/// embed), and persists the resulting DocumentChunks. Failures during that pipeline are
/// treated as transient and go through granular retry (Worker:MaxRetries, see ADR 0018) —
/// the job goes back to Pending and gets picked up again by a later poll, up to the
/// configured limit, before the job and Document are marked permanently Failed.
/// </summary>
public class ProcessingJobProcessor(
    SmartDocDbContext db,
    IFileStorage fileStorage,
    IAiServiceClient aiServiceClient,
    IConfiguration configuration,
    ILogger<ProcessingJobProcessor> logger)
{
    /// <summary>
    /// Recovers ProcessingJobs orphaned in Running by an ungraceful Worker shutdown (crash,
    /// kill, power loss) — the polling loop only ever picks up Pending jobs (see
    /// ProcessNextAsync below), so without this a job left in Running stays there forever,
    /// silently, with nothing retrying it or even logging that it happened (found via ADR
    /// 0021, fixed here — ADR 0023). Meant to run once at Worker startup, before polling
    /// begins: this project runs a single Worker instance (no distributed orchestration, see
    /// CLAUDE.md's scope guard), so any job still Running when a fresh instance starts up can
    /// only belong to a previous instance that's gone.
    ///
    /// Reuses RecordFailure rather than unconditionally resetting to Pending, deliberately —
    /// a job that reliably crashes the Worker every time it's attempted (a "poison pill")
    /// still needs to hit Worker:MaxRetries and land on Failed like any other repeated
    /// failure, not retry forever across restarts.
    /// </summary>
    /// <returns>How many orphaned jobs were found and recovered.</returns>
    public async Task<int> RecoverOrphanedJobsAsync(CancellationToken cancellationToken = default)
    {
        var orphanedJobs = await db.ProcessingJobs
            .Where(j => j.Status == ProcessingJobStatus.Running)
            .ToListAsync(cancellationToken);

        if (orphanedJobs.Count == 0)
        {
            return 0;
        }

        var maxRetries = configuration.GetValue("Worker:MaxRetries", 3);
        var now = DateTimeOffset.UtcNow;

        foreach (var job in orphanedJobs)
        {
            job.RecordFailure(
                "Job was left in Running state by an ungraceful Worker shutdown (crash, kill, or power loss).",
                now, maxRetries);

            if (job.Status == ProcessingJobStatus.Failed)
            {
                var document = await db.Documents.FindAsync([job.DocumentId], cancellationToken);
                document?.MarkAsFailed();
            }

            logger.LogWarning(
                "Recovered orphaned job {JobId} (document {DocumentId}), found in Running state at Worker startup. " +
                "New status: {Status} (attempt {RetryCount}).",
                job.Id, job.DocumentId, job.Status, job.RetryCount);
        }

        await db.SaveChangesAsync(cancellationToken);
        return orphanedJobs.Count;
    }

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
        // Checking the token, not the exception type, matters here: HttpClient.Timeout
        // firing on a slow /embed call throws TaskCanceledException too (it derives from
        // OperationCanceledException), but that's a transient failure like any other 4xx/5xx
        // - it belongs in the granular retry below, not propagating up. Left uncaught, it
        // crashes the BackgroundService and, with HostOptions.BackgroundServiceExceptionBehavior
        // defaulting to StopHost, takes down the entire Worker process (found processing a
        // 399-page calibration PDF whose batched /embed call exceeded 60s - see ADR 0021).
        // Only real cancellation of *this* token (host shutdown) should still propagate.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var maxRetries = configuration.GetValue("Worker:MaxRetries", 3);
            job.RecordFailure(ex.Message, DateTimeOffset.UtcNow, maxRetries);

            if (job.Status == ProcessingJobStatus.Failed)
            {
                document.MarkAsFailed();
                logger.LogError(
                    ex, "Processing job {JobId} for document {DocumentId} failed permanently after {RetryCount} attempt(s).",
                    job.Id, job.DocumentId, job.RetryCount);
            }
            else
            {
                // Document stays Processing — it's still being retried, not given up on yet.
                logger.LogWarning(
                    ex, "Processing job {JobId} for document {DocumentId} failed (attempt {RetryCount}/{MaxAttempts}), will retry.",
                    job.Id, job.DocumentId, job.RetryCount, maxRetries + 1);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
