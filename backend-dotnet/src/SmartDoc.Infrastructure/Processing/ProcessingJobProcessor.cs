using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartDoc.Domain.Enums;
using SmartDoc.Infrastructure.Persistence;

namespace SmartDoc.Infrastructure.Processing;

/// <summary>
/// Picks up and executes a single pending ProcessingJob. Extracted from the BackgroundService
/// polling loop so it can be exercised directly in tests without needing the hosted-service
/// machinery.
///
/// Phase 2 scope: proves the async Job/Worker mechanism itself (Pending -> Running -> Done,
/// Document Uploaded -> Processing -> Ready). There is no real content extraction yet — that
/// arrives in Phase 3 (parse/chunk/embed via the Python AI service), at which point the
/// placeholder work done here gets replaced with the real call.
/// </summary>
public class ProcessingJobProcessor(SmartDocDbContext db, ILogger<ProcessingJobProcessor> logger)
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

        var startedAt = DateTimeOffset.UtcNow;
        job.MarkAsRunning(startedAt);
        document?.MarkAsProcessing();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Processing job {JobId} for document {DocumentId} started.", job.Id, job.DocumentId);

        try
        {
            // Placeholder for the real work arriving in Phase 3 (parse/chunk/embed).
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            job.MarkAsDone(DateTimeOffset.UtcNow);
            document?.MarkAsReady();

            logger.LogInformation("Processing job {JobId} for document {DocumentId} completed.", job.Id, job.DocumentId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.MarkAsFailed(ex.Message, DateTimeOffset.UtcNow);
            document?.MarkAsFailed();

            logger.LogError(ex, "Processing job {JobId} for document {DocumentId} failed.", job.Id, job.DocumentId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
