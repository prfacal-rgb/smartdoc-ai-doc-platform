using FluentAssertions;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;

namespace SmartDoc.UnitTests.Domain;

public class ProcessingJobTests
{
    private static ProcessingJob CreateValidJob() => new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void Constructor_WithValidData_SetsPropertiesAndStartsAsPending()
    {
        var id = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var job = new ProcessingJob(id, documentId, createdAt);

        job.Id.Should().Be(id);
        job.DocumentId.Should().Be(documentId);
        job.CreatedAt.Should().Be(createdAt);
        job.UpdatedAt.Should().Be(createdAt);
        job.Status.Should().Be(ProcessingJobStatus.Pending);
        job.RetryCount.Should().Be(0);
        job.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        var act = () => new ProcessingJob(Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public void Constructor_WithEmptyDocumentId_Throws()
    {
        var act = () => new ProcessingJob(Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("documentId");
    }

    [Fact]
    public void MarkAsRunning_SetsStatusToRunningAndUpdatesTimestamp()
    {
        var job = CreateValidJob();
        var updatedAt = job.CreatedAt.AddMinutes(1);

        job.MarkAsRunning(updatedAt);

        job.Status.Should().Be(ProcessingJobStatus.Running);
        job.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void MarkAsDone_SetsStatusToDoneAndUpdatesTimestamp()
    {
        var job = CreateValidJob();
        var updatedAt = job.CreatedAt.AddMinutes(2);

        job.MarkAsDone(updatedAt);

        job.Status.Should().Be(ProcessingJobStatus.Done);
        job.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void MarkAsFailed_SetsStatusErrorMessageAndIncrementsRetryCount()
    {
        var job = CreateValidJob();
        var updatedAt = job.CreatedAt.AddMinutes(3);

        job.MarkAsFailed("boom", updatedAt);

        job.Status.Should().Be(ProcessingJobStatus.Failed);
        job.ErrorMessage.Should().Be("boom");
        job.RetryCount.Should().Be(1);
        job.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void MarkAsFailed_CalledTwice_IncrementsRetryCountEachTime()
    {
        var job = CreateValidJob();

        job.MarkAsFailed("first", DateTimeOffset.UtcNow);
        job.MarkAsFailed("second", DateTimeOffset.UtcNow);

        job.RetryCount.Should().Be(2);
        job.ErrorMessage.Should().Be("second");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MarkAsFailed_WithEmptyErrorMessage_Throws(string errorMessage)
    {
        var job = CreateValidJob();

        var act = () => job.MarkAsFailed(errorMessage, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("errorMessage");
    }

    [Fact]
    public void MarkAsFailed_WithErrorMessageExceedingMaxLength_Truncates()
    {
        var job = CreateValidJob();
        var tooLongMessage = new string('a', ProcessingJob.MaxErrorMessageLength + 100);

        job.MarkAsFailed(tooLongMessage, DateTimeOffset.UtcNow);

        job.ErrorMessage.Should().HaveLength(ProcessingJob.MaxErrorMessageLength);
    }

    [Fact]
    public void RecordFailure_WhenRetryCountStaysAtOrBelowMaxRetries_GoesBackToPending()
    {
        var job = CreateValidJob();
        var updatedAt = job.CreatedAt.AddMinutes(1);

        job.RecordFailure("transient error", updatedAt, maxRetries: 2);

        job.Status.Should().Be(ProcessingJobStatus.Pending);
        job.RetryCount.Should().Be(1);
        job.ErrorMessage.Should().Be("transient error");
        job.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void RecordFailure_WhenRetryCountExceedsMaxRetries_BecomesPermanentlyFailed()
    {
        var job = CreateValidJob();

        job.RecordFailure("first", DateTimeOffset.UtcNow, maxRetries: 2);
        job.RecordFailure("second", DateTimeOffset.UtcNow, maxRetries: 2);
        job.RecordFailure("third", DateTimeOffset.UtcNow, maxRetries: 2);

        job.Status.Should().Be(ProcessingJobStatus.Failed);
        job.RetryCount.Should().Be(3);
        job.ErrorMessage.Should().Be("third");
    }

    [Fact]
    public void RecordFailure_WithMaxRetriesZero_FailsPermanentlyOnFirstFailure()
    {
        var job = CreateValidJob();

        job.RecordFailure("no retries allowed", DateTimeOffset.UtcNow, maxRetries: 0);

        job.Status.Should().Be(ProcessingJobStatus.Failed);
        job.RetryCount.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordFailure_WithEmptyErrorMessage_Throws(string errorMessage)
    {
        var job = CreateValidJob();

        var act = () => job.RecordFailure(errorMessage, DateTimeOffset.UtcNow, maxRetries: 3);

        act.Should().Throw<ArgumentException>().WithParameterName("errorMessage");
    }

    [Fact]
    public void RecordFailure_WithNegativeMaxRetries_Throws()
    {
        var job = CreateValidJob();

        var act = () => job.RecordFailure("boom", DateTimeOffset.UtcNow, maxRetries: -1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxRetries");
    }

    [Fact]
    public void RecordFailure_WithErrorMessageExceedingMaxLength_Truncates()
    {
        var job = CreateValidJob();
        var tooLongMessage = new string('a', ProcessingJob.MaxErrorMessageLength + 100);

        job.RecordFailure(tooLongMessage, DateTimeOffset.UtcNow, maxRetries: 3);

        job.ErrorMessage.Should().HaveLength(ProcessingJob.MaxErrorMessageLength);
    }
}
