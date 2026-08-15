using FluentValidation;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Api.Features.Documents;

public sealed class CreateDocumentRequestValidator : AbstractValidator<CreateDocumentRequest>
{
    public CreateDocumentRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(Document.MaxFileNameLength);

        RuleFor(x => x.ContentType)
            .NotEmpty();

        RuleFor(x => x.StoragePath)
            .NotEmpty()
            .MaximumLength(Document.MaxStoragePathLength);
    }
}
