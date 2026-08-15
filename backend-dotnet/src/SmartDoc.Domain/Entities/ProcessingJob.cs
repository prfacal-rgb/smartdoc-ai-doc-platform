using SmartDoc.Domain.Enums;

namespace SmartDoc.Domain.Entities;

public class ProcessingJob
{
    public const int MaxErrorMessageLength = 2000;

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public ProcessingJobStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // EF Core
    private ProcessingJob()
    {
    }

    public ProcessingJob(Guid id, Guid documentId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id cannot be empty.", nameof(id));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId cannot be empty.", nameof(documentId));
        }

        Id = id;
        DocumentId = documentId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Status = ProcessingJobStatus.Pending;
        RetryCount = 0;
    }

    public void MarkAsRunning(DateTimeOffset updatedAt)
    {
        Status = ProcessingJobStatus.Running;
        UpdatedAt = updatedAt;
    }

    public void MarkAsDone(DateTimeOffset updatedAt)
    {
        Status = ProcessingJobStatus.Done;
        UpdatedAt = updatedAt;
    }

    public void MarkAsFailed(string errorMessage, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("ErrorMessage cannot be empty.", nameof(errorMessage));
        }

        Status = ProcessingJobStatus.Failed;
        ErrorMessage = errorMessage.Length > MaxErrorMessageLength
            ? errorMessage[..MaxErrorMessageLength]
            : errorMessage;
        RetryCount++;
        UpdatedAt = updatedAt;
    }
}
