using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartDoc.Application.AiService;

namespace SmartDoc.Infrastructure.AiService;

public class AiServiceClient(HttpClient httpClient) : IAiServiceClient
{
    // ai-service-python (FastAPI/pydantic) serializes as snake_case; this keeps the C# DTOs
    // idiomatic PascalCase while matching the wire format on both send and receive.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<IReadOnlyList<ParsedPage>> ParseAsync(
        Stream fileContent, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileContent);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);

        using var response = await httpClient.PostAsync("/parse", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ParseResponseDto>(JsonOptions, cancellationToken);
        return body!.Pages.Select(p => new ParsedPage(p.PageNumber, p.Text)).ToList();
    }

    public async Task<IReadOnlyList<TextChunk>> ChunkAsync(
        IReadOnlyList<ParsedPage> pages, CancellationToken cancellationToken = default)
    {
        var request = new ChunkRequestDto(pages.Select(p => new ParsedPageDto(p.PageNumber, p.Text)).ToList());

        using var response = await httpClient.PostAsJsonAsync("/chunk", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChunkResponseDto>(JsonOptions, cancellationToken);
        return body!.Chunks.Select(c => new TextChunk(c.ChunkIndex, c.PageNumber, c.Text)).ToList();
    }

    public async Task<EmbedResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var request = new EmbedRequestDto(texts.ToList());

        using var response = await httpClient.PostAsJsonAsync("/embed", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<EmbedResponseDto>(JsonOptions, cancellationToken);
        var embeddings = body!.Embeddings.Select(e => e.ToArray()).ToList();
        return new EmbedResult(embeddings, body.Model, body.Dimensions);
    }

    private sealed record ParsedPageDto(int PageNumber, string Text);
    private sealed record ParseResponseDto(List<ParsedPageDto> Pages);

    private sealed record ChunkRequestDto(List<ParsedPageDto> Pages);
    private sealed record ChunkDto(int ChunkIndex, int PageNumber, string Text);
    private sealed record ChunkResponseDto(List<ChunkDto> Chunks);

    private sealed record EmbedRequestDto(List<string> Texts);
    private sealed record EmbedResponseDto(List<List<float>> Embeddings, string Model, int Dimensions);
}
