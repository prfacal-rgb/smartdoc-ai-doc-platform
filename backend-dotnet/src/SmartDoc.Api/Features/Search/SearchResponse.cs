namespace SmartDoc.Api.Features.Search;

public sealed record SearchResultItem(string FileName, int PageNumber, string Text, double Distance);

public sealed record SearchResponse(List<SearchResultItem> Results);
