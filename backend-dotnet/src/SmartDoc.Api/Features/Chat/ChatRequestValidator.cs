using FluentValidation;

namespace SmartDoc.Api.Features.Chat;

public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Question).NotEmpty();
    }
}
