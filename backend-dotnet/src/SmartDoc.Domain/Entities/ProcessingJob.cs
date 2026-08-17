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

    /// <summary>
    /// Marks the job as permanently failed, with no retry. Reserved for failures that are
    /// structurally unrecoverable (e.g. the referenced Document no longer exists) — retrying
    /// them would just fail again the same way. Transient failures during processing itself
    /// go through <see cref="RecordFailure"/> instead.
    /// </summary>
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

    /// <summary>
    /// Records a (presumed transient) processing failure and decides whether the job goes
    /// back to Pending for another attempt or becomes permanently Failed, based on
    /// <paramref name="maxRetries"/> — the maximum number of retries allowed *after* the
    /// initial attempt. E.g. maxRetries=3 allows up to 4 total attempts (1 initial + 3
    /// retries) before giving up. See ADR 0018.
    /// </summary>
    public void RecordFailure(string errorMessage, DateTimeOffset updatedAt, int maxRetries)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("ErrorMessage cannot be empty.", nameof(errorMessage));
        }

        if (maxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetries), maxRetries, "MaxRetries cannot be negative.");
        }

        RetryCount++;
        ErrorMessage = errorMessage.Length > MaxErrorMessageLength
            ? errorMessage[..MaxErrorMessageLength]
            : errorMessage;
        UpdatedAt = updatedAt;
        Status = RetryCount <= maxRetries ? ProcessingJobStatus.Pending : ProcessingJobStatus.Failed;
    }
}
