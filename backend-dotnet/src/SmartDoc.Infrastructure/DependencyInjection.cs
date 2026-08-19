using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartDoc.Application.AiService;
using SmartDoc.Application.Storage;
using SmartDoc.Infrastructure.AiService;
using SmartDoc.Infrastructure.Persistence;
using SmartDoc.Infrastructure.Processing;
using SmartDoc.Infrastructure.Search;
using SmartDoc.Infrastructure.Storage;

namespace SmartDoc.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Connection string 'Postgres' is not configured. Set ConnectionStrings:Postgres.");

        services.AddDbContext<SmartDocDbContext>(options =>
            options.UseNpgsql(connectionString, o => o.UseVector()));

        services.AddScoped<ProcessingJobProcessor>();
        services.AddScoped<SimilaritySearchService>();

        AddFileStorage(services, configuration);
        AddAiServiceClient(services, configuration);

        return services;
    }

    private static void AddAiServiceClient(IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["AiService:BaseUrl"]
            ?? throw new InvalidOperationException("AiService:BaseUrl is not configured.");

        services.AddHttpClient<IAiServiceClient, AiServiceClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            // A single /embed call batches every chunk of a document at once - measured
            // ~600-700ms/chunk against local Ollama (see ADR 0021), so a large PDF needs
            // several minutes. Raised rather than tuned precisely: nothing in this project's
            // traffic is latency-sensitive enough to need a tighter ceiling, and the
            // ProcessingJobProcessor catch above now retries a real timeout gracefully
            // instead of crashing the Worker either way.
            client.Timeout = TimeSpan.FromSeconds(600);
        });
    }

    private static void AddFileStorage(IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration["Minio:Endpoint"]
            ?? throw new InvalidOperationException("Minio:Endpoint is not configured.");
        var accessKey = configuration["Minio:AccessKey"]
            ?? throw new InvalidOperationException("Minio:AccessKey is not configured.");
        var secretKey = configuration["Minio:SecretKey"]
            ?? throw new InvalidOperationException("Minio:SecretKey is not configured.");
        var bucketName = configuration["Minio:BucketName"]
            ?? throw new InvalidOperationException("Minio:BucketName is not configured.");

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            accessKey,
            secretKey,
            new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true, // required for MinIO / non-AWS S3-compatible endpoints
            }));

        services.AddScoped<IFileStorage>(sp => new MinioFileStorage(sp.GetRequiredService<IAmazonS3>(), bucketName));
    }
}
