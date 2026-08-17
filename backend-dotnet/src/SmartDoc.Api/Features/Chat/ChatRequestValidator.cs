using FluentValidation;

namespace SmartDoc.Api.Features.Chat;

public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        RuleFor(x => x.Question).NotEmpty();
    }
}
