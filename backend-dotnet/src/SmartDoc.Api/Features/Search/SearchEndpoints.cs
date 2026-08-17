using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using SmartDoc.Application.AiService;
using SmartDoc.Infrastructure.Search;

namespace SmartDoc.Api.Features.Search;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/search", SearchAsync).WithTags("Search").RequireAuthorization();
    }

    private static async Task<Results<Ok<SearchResponse>, ValidationProblem>> SearchAsync(
        SearchRequest request,
        IValidator<SearchRequest> validator,
        IAiServiceClient aiServiceClient,
        SimilaritySearchService similaritySearchService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        var topK = request.TopK ?? configuration.GetValue("Rag:DefaultTopK", 5);

        var embedResult = await aiServiceClient.EmbedAsync([request.Query], cancellationToken);
        var matches = await similaritySearchService.SearchAsync(embedResult.Embeddings[0], topK, cancellationToken);

        var results = matches
            .Select(m => new SearchResultItem(m.FileName, m.PageNumber, m.Text, m.Distance))
            .ToList();

        return TypedResults.Ok(new SearchResponse(results));
    }
}
