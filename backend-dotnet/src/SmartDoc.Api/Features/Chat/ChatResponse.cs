namespace SmartDoc.Api.Features.Chat;

public sealed record Citation(string FileName, int PageNumber);

public sealed record ChatResponse(Guid ConversationId, string Answer, List<Citation> Sources);
