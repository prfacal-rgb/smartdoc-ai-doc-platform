using FluentAssertions;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;

namespace SmartDoc.UnitTests.Domain;

public class DocumentTests
{
    private static Document CreateValidDocument() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "report.pdf", "application/pdf", "/storage/report.pdf", DateTimeOffset.UtcNow);

    [Fact]
    public void Constructor_WithValidData_SetsPropertiesAndStartsAsUploaded()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var document = new Document(id, userId, "report.pdf", "application/pdf", "/storage/report.pdf", createdAt);

        document.Id.Should().Be(id);
        document.UserId.Should().Be(userId);
        document.FileName.Should().Be("report.pdf");
        document.ContentType.Should().Be("application/pdf");
        document.StoragePath.Should().Be("/storage/report.pdf");
        document.CreatedAt.Should().Be(createdAt);
        document.Status.Should().Be(DocumentStatus.Uploaded);
    }

    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        var act = () => new Document(Guid.Empty, Guid.NewGuid(), "report.pdf", "application/pdf", "/storage/report.pdf", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public void Constructor_WithEmptyUserId_Throws()
    {
        var act = () => new Document(Guid.NewGuid(), Guid.Empty, "report.pdf", "application/pdf", "/storage/report.pdf", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("userId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyFileName_Throws(string fileName)
    {
        var act = () => new Document(Guid.NewGuid(), Guid.NewGuid(), fileName, "application/pdf", "/storage/report.pdf", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("fileName");
    }

    [Fact]
    public void Constructor_WithFileNameExceedingMaxLength_Throws()
    {
        var tooLongFileName = new string('a', Document.MaxFileNameLength + 1);

        var act = () => new Document(Guid.NewGuid(), Guid.NewGuid(), tooLongFileName, "application/pdf", "/storage/report.pdf", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("fileName");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyContentType_Throws(string contentType)
    {
        var act = () => new Document(Guid.NewGuid(), Guid.NewGuid(), "report.pdf", contentType, "/storage/report.pdf", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("contentType");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyStoragePath_Throws(string storagePath)
    {
        var act = () => new Document(Guid.NewGuid(), Guid.NewGuid(), "report.pdf", "application/pdf", storagePath, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("storagePath");
    }

    [Fact]
    public void Constructor_WithStoragePathExceedingMaxLength_Throws()
    {
        var tooLongStoragePath = new string('a', Document.MaxStoragePathLength + 1);

        var act = () => new Document(Guid.NewGuid(), Guid.NewGuid(), "report.pdf", "application/pdf", tooLongStoragePath, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("storagePath");
    }

    [Fact]
    public void MarkAsProcessing_SetsStatusToProcessing()
    {
        var document = CreateValidDocument();

        document.MarkAsProcessing();

        document.Status.Should().Be(DocumentStatus.Processing);
    }

    [Fact]
    public void MarkAsReady_SetsStatusToReady()
    {
        var document = CreateValidDocument();

        document.MarkAsReady();

        document.Status.Should().Be(DocumentStatus.Ready);
    }

    [Fact]
    public void MarkAsFailed_SetsStatusToFailed()
    {
        var document = CreateValidDocument();

        document.MarkAsFailed();

        document.Status.Should().Be(DocumentStatus.Failed);
    }
}
