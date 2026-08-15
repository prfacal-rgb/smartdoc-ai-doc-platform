using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;
using SmartDoc.Infrastructure.Persistence;

namespace SmartDoc.Api.Features.Documents;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents").WithTags("Documents");

        group.MapPost("/", CreateDocumentAsync);
        group.MapGet("/", GetDocumentsAsync);
        group.MapGet("/{id:guid}", GetDocumentByIdAsync);
        group.MapDelete("/{id:guid}", DeleteDocumentAsync);
    }

    private static async Task<Results<Accepted<DocumentResponse>, ValidationProblem, NotFound<ProblemDetails>>> CreateDocumentAsync(
        CreateDocumentRequest request,
        IValidator<CreateDocumentRequest> validator,
        SmartDocDbContext db,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken);
        if (!userExists)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "User not found",
                Detail = $"No user exists with id '{request.UserId}'.",
            });
        }

        var now = DateTimeOffset.UtcNow;
        var document = new Document(
            Guid.NewGuid(), request.UserId, request.FileName, request.ContentType, request.StoragePath, now);
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
        Guid id, SmartDocDbContext db, CancellationToken cancellationToken)
    {
        var document = await db.Documents.FindAsync([id], cancellationToken);
        if (document is null)
        {
            return TypedResults.NotFound();
        }

        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }
}
