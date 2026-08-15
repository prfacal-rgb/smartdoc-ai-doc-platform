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

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<ProcessingJobProcessor>();

                try
                {
                    await processor.ProcessNextAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Unhandled error while processing a job.");
                }
            }

            await Task.Delay(pollingInterval, stoppingToken);
        }
    }
}
