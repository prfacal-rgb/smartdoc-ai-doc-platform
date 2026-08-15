namespace SmartDoc.Application.Storage;

/// <summary>
/// Object storage port. MinIO in dev (SmartDoc.Infrastructure/Storage/MinioFileStorage),
/// swappable for a cloud-equivalent implementation (e.g. AWS S3) without touching callers —
/// see PROJECT.md §"Storage de archivos".
/// </summary>
public interface IFileStorage
{
    /// <returns>The storage path/key the content was saved under (persisted as Document.StoragePath).</returns>
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default);

    Task<Stream> GetAsync(string storagePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}
