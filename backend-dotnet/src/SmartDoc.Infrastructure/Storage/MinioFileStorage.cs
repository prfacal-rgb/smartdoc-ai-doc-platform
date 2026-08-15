using Amazon.S3;
using Amazon.S3.Model;
using SmartDoc.Application.Storage;

namespace SmartDoc.Infrastructure.Storage;

public class MinioFileStorage(IAmazonS3 s3Client, string bucketName) : IFileStorage
{
    public async Task<string> SaveAsync(
        Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        // Prefixed with a Guid to avoid key collisions between documents with the same
        // FileName; the full key is what gets persisted as Document.StoragePath.
        var key = $"{Guid.NewGuid():N}/{fileName}";

        await s3Client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
            },
            cancellationToken);

        return key;
    }

    public async Task<Stream> GetAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var response = await s3Client.GetObjectAsync(bucketName, storagePath, cancellationToken);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        await s3Client.DeleteObjectAsync(bucketName, storagePath, cancellationToken);
    }
}
