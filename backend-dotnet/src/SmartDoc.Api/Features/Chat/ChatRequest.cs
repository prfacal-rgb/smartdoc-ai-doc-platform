namespace SmartDoc.Api.Features.Chat;

public sealed record ChatRequest(string Question, Guid? ConversationId);
