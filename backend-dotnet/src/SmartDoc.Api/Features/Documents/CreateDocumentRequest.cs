namespace SmartDoc.Api.Features.Documents;

/// <summary>
/// Phase 1 scope: metadata only, no real file bytes. Real upload + object storage
/// (StoragePath backed by an actual file) arrives together with the async processing
/// pipeline (Phase 2/3).
/// </summary>
public sealed record CreateDocumentRequest(Guid UserId, string FileName, string ContentType, string StoragePath);
