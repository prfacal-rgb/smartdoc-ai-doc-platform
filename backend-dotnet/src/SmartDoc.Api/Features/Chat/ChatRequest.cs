namespace SmartDoc.Api.Features.Chat;

public sealed record ChatRequest(Guid UserId, string Question, Guid? ConversationId);
