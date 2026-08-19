using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartDoc.Infrastructure.Processing;

namespace SmartDoc.Worker;

/// <summary>
/// Polls for pending ProcessingJobs at a fixed interval (Worker:PollingIntervalSeconds,
/// default 5s — see PROJECT.md's "polling simple" default). The actual job-picking and
/// processing logic lives in ProcessingJobProcessor (SmartDoc.Infrastructure), kept
/// separate so it can be tested without this loop.
/// </summary>
public class ProcessingJobPollingWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ProcessingJobPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingInterval = TimeSpan.FromSeconds(configuration.GetValue("Worker:PollingIntervalSeconds", 5));

        logger.LogInformation("ProcessingJobPollingWorker starting, polling every {Interval}.", pollingInterval);

        using (var startupScope = scopeFactory.CreateScope())
        {
            // Once, before polling begins: a job left Running by a previous instance that
            // crashed/was killed never gets picked up otherwise (ProcessNextAsync only looks
            // at Pending) - see ADR 0023.
            var processor = startupScope.ServiceProvider.GetRequiredService<ProcessingJobProcessor>();
            var recovered = await processor.RecoverOrphanedJobsAsync(stoppingToken);
            if (recovered > 0)
            {
                logger.LogWarning("Recovered {Count} job(s) orphaned in Running state from a previous run.", recovered);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<ProcessingJobProcessor>();

                try
                {
                    await processor.ProcessNextAsync(stoppingToken);
                }
                // Same reasoning as ProcessingJobProcessor's own catch (ADR 0021/0023):
                // checking the token, not the exception type, matters because a cancellation-
                // linked timeout below this loop (e.g. a DB command timeout, which can throw
                // an OperationCanceledException-derived exception with the token unsignaled)
                // is a transient failure, not a real shutdown - it shouldn't crash the Worker
                // via HostOptions.BackgroundServiceExceptionBehavior = StopHost either.
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Unhandled error while processing a job.");
                }
            }

            await Task.Delay(pollingInterval, stoppingToken);
        }
    }
}
