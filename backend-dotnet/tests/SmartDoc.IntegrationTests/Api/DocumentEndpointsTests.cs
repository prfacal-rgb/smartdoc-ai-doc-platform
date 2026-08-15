using System.Net;
using System.Net.Http.Json;
using Amazon.S3;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartDoc.Api.Features.Documents;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;
using SmartDoc.IntegrationTests.Persistence;

namespace SmartDoc.IntegrationTests.Api;

/// <summary>
/// End-to-end tests against the real Minimal API pipeline (routing, validation, EF Core,
/// Postgres, MinIO) via WebApplicationFactory. Each test gets its own throwaway User created
/// directly through SmartDocDbContext (there is no Users endpoint by design), and cleans up
/// everything it created (DB rows and any uploaded object storage content).
/// </summary>
public class DocumentEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly DatabaseFixture _dbFixture = new();
    private HttpClient _client = null!;
    private User _testUser = null!;

    public DocumentEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();

        _testUser = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);

        await using var db = _dbFixture.CreateContext();
        db.Users.Add(_testUser);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = _dbFixture.CreateContext();

        var documents = await db.Documents.Where(d => d.UserId == _testUser.Id).ToListAsync();
        if (documents.Count > 0)
        {
            var s3Client = _factory.Services.GetRequiredService<IAmazonS3>();
            foreach (var document in documents)
            {
                await s3Client.DeleteObjectAsync("smartdoc-documents", document.StoragePath);
            }

            // ProcessingJobs cascade-delete with their Document (see ProcessingJobConfiguration).
            db.Documents.RemoveRange(documents);
            await db.SaveChangesAsync();
        }

        db.Users.Remove(_testUser);
        await db.SaveChangesAsync();
    }

    private static MultipartFormDataContent CreatePdfUploadContent(
        Guid userId, string fileName = "report.pdf", string contentType = "application/pdf", string text = "%PDF-1.4 fake content")
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(userId.ToString()), "userId" },
        };

        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(text));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        return content;
    }

    [Fact]
    public async Task PostDocuments_WithValidRequest_ReturnsAcceptedWithLocationAndBody()
    {
        using var content = CreatePdfUploadContent(_testUser.Id);

        var response = await _client.PostAsync("/api/documents", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        body.Should().NotBeNull();
        body!.UserId.Should().Be(_testUser.Id);
        body.FileName.Should().Be("report.pdf");
        body.Status.Should().Be("Uploaded");
    }

    [Fact]
    public async Task PostDocuments_WithValidRequest_CreatesAPendingProcessingJob()
    {
        using var content = CreatePdfUploadContent(_testUser.Id);

        var response = await _client.PostAsync("/api/documents", content);
        var created = await response.Content.ReadFromJsonAsync<DocumentResponse>();

        await using var db = _dbFixture.CreateContext();
        var job = await db.ProcessingJobs.SingleAsync(j => j.DocumentId == created!.Id);

        job.Status.Should().Be(ProcessingJobStatus.Pending);
        job.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task PostDocuments_WithValidRequest_SavesFileContentToObjectStorage()
    {
        const string fileText = "%PDF-1.4 smoke test content";
        using var content = CreatePdfUploadContent(_testUser.Id, text: fileText);

        var response = await _client.PostAsync("/api/documents", content);
        var created = await response.Content.ReadFromJsonAsync<DocumentResponse>();

        var s3Client = _factory.Services.GetRequiredService<IAmazonS3>();
        using var stored = await s3Client.GetObjectAsync("smartdoc-documents", created!.StoragePath);
        using var reader = new StreamReader(stored.ResponseStream);
        var storedText = await reader.ReadToEndAsync();

        storedText.Should().Be(fileText);
    }

    [Fact]
    public async Task PostDocuments_WithNonPdfContentType_ReturnsValidationProblem()
    {
        using var content = CreatePdfUploadContent(_testUser.Id, fileName: "report.txt", contentType: "text/plain");

        var response = await _client.PostAsync("/api/documents", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostDocuments_WithEmptyFile_ReturnsValidationProblem()
    {
        using var content = CreatePdfUploadContent(_testUser.Id, text: "");

        var response = await _client.PostAsync("/api/documents", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostDocuments_WithNonExistentUserId_ReturnsNotFound()
    {
        using var content = CreatePdfUploadContent(Guid.NewGuid());

        var response = await _client.PostAsync("/api/documents", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDocumentById_AfterCreate_ReturnsSameDocument()
    {
        using var content = CreatePdfUploadContent(_testUser.Id);
        var createResponse = await _client.PostAsync("/api/documents", content);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentResponse>();

        var response = await _client.GetAsync($"/api/documents/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        body!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetDocumentById_WithNonExistentId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDocuments_AfterCreate_ContainsCreatedDocument()
    {
        using var content = CreatePdfUploadContent(_testUser.Id);
        var createResponse = await _client.PostAsync("/api/documents", content);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentResponse>();

        var response = await _client.GetAsync("/api/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<DocumentResponse>>();
        body!.Should().Contain(d => d.Id == created!.Id);
    }

    [Fact]
    public async Task DeleteDocument_AfterCreate_RemovesItAndSubsequentGetReturnsNotFound()
    {
        using var content = CreatePdfUploadContent(_testUser.Id);
        var createResponse = await _client.PostAsync("/api/documents", content);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentResponse>();

        var deleteResponse = await _client.DeleteAsync($"/api/documents/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/documents/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteDocument_AfterCreate_AlsoRemovesFileFromObjectStorage()
    {
        using var content = CreatePdfUploadContent(_testUser.Id);
        var createResponse = await _client.PostAsync("/api/documents", content);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentResponse>();

        await _client.DeleteAsync($"/api/documents/{created!.Id}");

        var s3Client = _factory.Services.GetRequiredService<IAmazonS3>();
        var act = async () => await s3Client.GetObjectAsync("smartdoc-documents", created.StoragePath);

        await act.Should().ThrowAsync<AmazonS3Exception>();
    }

    [Fact]
    public async Task DeleteDocument_WithNonExistentId_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"/api/documents/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
