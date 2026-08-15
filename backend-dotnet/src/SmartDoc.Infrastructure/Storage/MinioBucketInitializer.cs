using Amazon.S3;
using Amazon.S3.Util;

namespace SmartDoc.Infrastructure.Storage;

/// <summary>
/// Ensures the configured bucket exists before first use. Idempotent — same spirit as
/// SmartDocDbContextSeeder, safe to run on every startup.
/// </summary>
public static class MinioBucketInitializer
{
    public static async Task EnsureBucketExistsAsync(
        IAmazonS3 s3Client, string bucketName, CancellationToken cancellationToken = default)
    {
        var exists = await AmazonS3Util.DoesS3BucketExistV2Async(s3Client, bucketName);
        if (!exists)
        {
            await s3Client.PutBucketAsync(bucketName, cancellationToken);
        }
    }
}
