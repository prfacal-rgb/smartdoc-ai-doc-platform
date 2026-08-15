using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Api.Features.Documents;
using SmartDoc.Domain.Entities;
using SmartDoc.Domain.Enums;
using SmartDoc.IntegrationTests.Persistence;

namespace SmartDoc.IntegrationTests.Api;

/// <summary>
/// End-to-end tests against the real Minimal API pipeline (routing, validation, EF Core,
/// Postgres) via WebApplicationFactory. Each test gets its own throwaway User created
/// directly through SmartDocDbContext (there is no Users endpoint by design), and cleans up
/// everything it created.
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

        // ProcessingJobs cascade-delete with their Document (ADR — see ProcessingJobConfiguration),
        // so removing the Documents is enough; no separate cleanup needed for the jobs.
        db.Documents.RemoveRange(db.Documents.Where(d => d.UserId == _testUser.Id));
        await db.SaveChangesAsync();

        db.Users.Remove(_testUser);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PostDocuments_WithValidRequest_ReturnsAcceptedWithLocationAndBody()
    {
        var request = new CreateDocumentRequest(_testUser.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf");

        var response = await _client.PostAsJsonAsync("/api/documents", request);

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
        var request = new CreateDocumentRequest(_testUser.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf");

        var response = await _client.PostAsJsonAsync("/api/documents", request);
        var created = await response.Content.ReadFromJsonAsync<DocumentResponse>();

        await using var db = _dbFixture.CreateContext();
        var job = await db.ProcessingJobs.SingleAsync(j => j.DocumentId == created!.Id);

        job.Status.Should().Be(ProcessingJobStatus.Pending);
        job.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task PostDocuments_WithEmptyFileName_ReturnsValidationProblem()
    {
        var request = new CreateDocumentRequest(_testUser.Id, "", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf");

        var response = await _client.PostAsJsonAsync("/api/documents", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostDocuments_WithNonExistentUserId_ReturnsNotFound()
    {
        var request = new CreateDocumentRequest(Guid.NewGuid(), "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf");

        var response = await _client.PostAsJsonAsync("/api/documents", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDocumentById_AfterCreate_ReturnsSameDocument()
    {
        var createRequest = new CreateDocumentRequest(_testUser.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf");
        var createResponse = await _client.PostAsJsonAsync("/api/documents", createRequest);
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
        var createRequest = new CreateDocumentRequest(_testUser.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf");
        var createResponse = await _client.PostAsJsonAsync("/api/documents", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentResponse>();

        var response = await _client.GetAsync("/api/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<DocumentResponse>>();
        body!.Should().Contain(d => d.Id == created!.Id);
    }

    [Fact]
    public async Task DeleteDocument_AfterCreate_RemovesItAndSubsequentGetReturnsNotFound()
    {
        var createRequest = new CreateDocumentRequest(_testUser.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf");
        var createResponse = await _client.PostAsJsonAsync("/api/documents", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentResponse>();

        var deleteResponse = await _client.DeleteAsync($"/api/documents/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/documents/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteDocument_WithNonExistentId_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"/api/documents/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
