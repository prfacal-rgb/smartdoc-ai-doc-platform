using SmartDoc.Domain.Entities;

namespace SmartDoc.Api.Features.Chat;

public sealed record MessageResponse(Guid Id, string Role, string Content, DateTimeOffset CreatedAt)
{
    public static MessageResponse FromEntity(Message message) =>
        new(message.Id, message.Role.ToString(), message.Content, message.CreatedAt);
}

public sealed record ConversationHistoryResponse(Guid ConversationId, List<MessageResponse> Messages);
