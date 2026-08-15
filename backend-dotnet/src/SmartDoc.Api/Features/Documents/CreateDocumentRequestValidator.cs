using FluentValidation;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Api.Features.Documents;

public sealed class CreateDocumentRequestValidator : AbstractValidator<CreateDocumentRequest>
{
    // PDF-only in the MVP — see PROJECT.md §8 ("Tipos de archivo soportados en MVP: PDF
    // únicamente"), enforced here rather than left as an unstated assumption.
    private const string AllowedContentType = "application/pdf";

    public CreateDocumentRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.File)
            .NotNull()
            .WithMessage("File is required.");

        When(x => x.File is not null, () =>
        {
            RuleFor(x => x.File.Length)
                .GreaterThan(0)
                .WithMessage("File cannot be empty.");

            RuleFor(x => x.File.ContentType)
                .Equal(AllowedContentType)
                .WithMessage($"Only {AllowedContentType} files are supported.");

            RuleFor(x => x.File.FileName)
                .NotEmpty()
                .MaximumLength(Document.MaxFileNameLength);
        });
    }
}
