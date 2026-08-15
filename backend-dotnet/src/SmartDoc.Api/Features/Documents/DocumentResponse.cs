using SmartDoc.Domain.Entities;

namespace SmartDoc.Api.Features.Documents;

public sealed record DocumentResponse(
    Guid Id,
    Guid UserId,
    string FileName,
    string ContentType,
    string StoragePath,
    string Status,
    DateTimeOffset CreatedAt)
{
    public static DocumentResponse FromEntity(Document document) => new(
        document.Id,
        document.UserId,
        document.FileName,
        document.ContentType,
        document.StoragePath,
        document.Status.ToString(),
        document.CreatedAt);
}
