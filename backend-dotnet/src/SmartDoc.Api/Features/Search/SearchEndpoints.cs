using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using SmartDoc.Api;
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
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // RagDistanceLog.CategoryName (not ILogger<SearchEndpoints> — static classes can't
        // be a generic type argument, CS0718) routes this to its own file via a Serilog
        // sub-logger (ADR 0020), on top of the general app log.
        var logger = loggerFactory.CreateLogger(RagDistanceLog.CategoryName);

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        var topK = request.TopK ?? configuration.GetValue("Rag:DefaultTopK", 5);

        var embedResult = await aiServiceClient.EmbedAsync([request.Query], cancellationToken);
        var matches = await similaritySearchService.SearchAsync(embedResult.Embeddings[0], topK, cancellationToken);

        // Same rationale as ChatEndpoints (ADR 0020/0016): even though Distance is already in
        // the response here, logging it keeps the signal available without depending on every
        // caller inspecting/storing the raw response.
        logger.LogInformation(
            "Similarity search returned {MatchCount} candidate(s). Distances: {@Distances}",
            matches.Count, matches.Select(m => Math.Round(m.Distance, 4)));

        var results = matches
            .Select(m => new SearchResultItem(m.FileName, m.PageNumber, m.Text, m.Distance))
            .ToList();

        return TypedResults.Ok(new SearchResponse(results));
    }
}
