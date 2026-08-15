using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Application.Storage;
using SmartDoc.Domain.Entities;
using SmartDoc.Infrastructure.Persistence;

namespace SmartDoc.Api.Features.Documents;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents").WithTags("Documents");

        // File-upload endpoints get anti-forgery metadata attached automatically since
        // .NET 8 (ASP.NET Core requires app.UseAntiforgery() otherwise). Not needed here:
        // this is a token/API-consumed backend, not a cookie-authenticated browser form.
        group.MapPost("/", CreateDocumentAsync).DisableAntiforgery();
        group.MapGet("/", GetDocumentsAsync);
        group.MapGet("/{id:guid}", GetDocumentByIdAsync);
        group.MapDelete("/{id:guid}", DeleteDocumentAsync);
    }

    private static async Task<Results<Accepted<DocumentResponse>, ValidationProblem, NotFound<ProblemDetails>>> CreateDocumentAsync(
        IFormFile file,
        [FromForm] Guid userId,
        IValidator<CreateDocumentRequest> validator,
        SmartDocDbContext db,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        var request = new CreateDocumentRequest(userId, file);
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == userId, cancellationToken);
        if (!userExists)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "User not found",
                Detail = $"No user exists with id '{userId}'.",
            });
        }

        var fileName = Path.GetFileName(file.FileName);

        await using var stream = file.OpenReadStream();
        var storagePath = await fileStorage.SaveAsync(stream, fileName, file.ContentType, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var document = new Document(Guid.NewGuid(), userId, fileName, file.ContentType, storagePath, now);
        var processingJob = new ProcessingJob(Guid.NewGuid(), document.Id, now);

        db.Documents.Add(document);
        db.ProcessingJobs.Add(processingJob);
        await db.SaveChangesAsync(cancellationToken);

        // 202: the document is stored but not processed yet — the Worker picks up the
        // ProcessingJob asynchronously. Location points to where the client can poll status.
        return TypedResults.Accepted($"/api/documents/{document.Id}", DocumentResponse.FromEntity(document));
    }

    private static async Task<Ok<List<DocumentResponse>>> GetDocumentsAsync(
        SmartDocDbContext db, CancellationToken cancellationToken)
    {
        var documents = await db.Documents
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(documents.Select(DocumentResponse.FromEntity).ToList());
    }

    private static async Task<Results<Ok<DocumentResponse>, NotFound>> GetDocumentByIdAsync(
        Guid id, SmartDocDbContext db, CancellationToken cancellationToken)
    {
        var document = await db.Documents.FindAsync([id], cancellationToken);

        return document is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(DocumentResponse.FromEntity(document));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteDocumentAsync(
        Guid id, SmartDocDbContext db, IFileStorage fileStorage, CancellationToken cancellationToken)
    {
        var document = await db.Documents.FindAsync([id], cancellationToken);
        if (document is null)
        {
            return TypedResults.NotFound();
        }

        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        // Best-effort: the DB row is already gone (source of truth for "does this document
        // exist"), so a storage delete failure here doesn't get retried — an orphaned object
        // in MinIO is a cheap, non-urgent cleanup issue, not a correctness one.
        await fileStorage.DeleteAsync(document.StoragePath, cancellationToken);

        return TypedResults.NoContent();
    }
}
