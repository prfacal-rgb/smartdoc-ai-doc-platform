namespace SmartDoc.Application.AiService;

public sealed record ParsedPage(int PageNumber, string Text);

public sealed record TextChunk(int ChunkIndex, int PageNumber, string Text);

public sealed record EmbedResult(IReadOnlyList<float[]> Embeddings, string Model, int Dimensions);

public sealed record GenerateResult(string Answer, string Model);

/// <summary>
/// Port to the Python AI service (see PROJECT.md §4.D and CLAUDE.md's .NET↔Python contract:
/// stateless, called over internal HTTP, never exposed publicly). Implemented by
/// SmartDoc.Infrastructure/AiService/AiServiceClient.
/// </summary>
public interface IAiServiceClient
{
    Task<IReadOnlyList<ParsedPage>> ParseAsync(
        Stream fileContent, string fileName, string contentType, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TextChunk>> ChunkAsync(IReadOnlyList<ParsedPage> pages, CancellationToken cancellationToken = default);

    Task<EmbedResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// contextChunks is plain text only — no file/page metadata is sent, and none comes
    /// back. .NET already knows which chunks it retrieved and builds citations
    /// deterministically from that metadata (ADR 0014), not from the LLM's own text.
    /// </summary>
    Task<GenerateResult> GenerateAsync(
        string question, IReadOnlyList<string> contextChunks, CancellationToken cancellationToken = default);
}
