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
            client.Timeout = TimeSpan.FromSeconds(60); // embeddings/parsing can take a while
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
