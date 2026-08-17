using FluentValidation;

namespace SmartDoc.Api.Features.Search;

public sealed class SearchRequestValidator : AbstractValidator<SearchRequest>
{
    private const int MaxTopK = 50;

    public SearchRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty();

        RuleFor(x => x.TopK)
            .InclusiveBetween(1, MaxTopK)
            .When(x => x.TopK.HasValue);
    }
}
