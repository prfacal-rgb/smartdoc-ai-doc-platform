namespace SmartDoc.Api.Features.Documents;

/// <summary>
/// Not bound directly from HTTP — built from the individually-bound IFormFile/UserId
/// parameters in DocumentEndpoints so FluentValidation can validate them as a unit
/// (see ADR — file upload now goes through IFileStorage/MinIO instead of a plain
/// metadata-only JSON body).
/// </summary>
public sealed record CreateDocumentRequest(Guid UserId, IFormFile File);
