using SmartDoc.Domain.Enums;

namespace SmartDoc.Domain.Entities;

public class Document
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string StoragePath { get; private set; } = null!;
    public DocumentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core
    private Document()
    {
    }

    public Document(
        Guid id,
        Guid userId,
        string fileName,
        string contentType,
        string storagePath,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id cannot be empty.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("FileName cannot be empty.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("ContentType cannot be empty.", nameof(contentType));
        }

        if (string.IsNullOrWhiteSpace(storagePath))
        {
            throw new ArgumentException("StoragePath cannot be empty.", nameof(storagePath));
        }

        Id = id;
        UserId = userId;
        FileName = fileName;
        ContentType = contentType;
        StoragePath = storagePath;
        CreatedAt = createdAt;
        Status = DocumentStatus.Uploaded;
    }

    public void MarkAsProcessing()
    {
        Status = DocumentStatus.Processing;
    }

    public void MarkAsReady()
    {
        Status = DocumentStatus.Ready;
    }

    public void MarkAsFailed()
    {
        Status = DocumentStatus.Failed;
    }
}
