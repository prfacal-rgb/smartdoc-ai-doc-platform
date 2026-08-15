namespace SmartDoc.Domain.Entities;

public class DocumentChunk
{
    /// <summary>
    /// Fixed at 768 (nomic-embed-text) — see ADR 0010/0012. Changing embedding models to a
    /// different dimension is not a config swap: it requires a schema migration on this
    /// column plus re-embedding every existing chunk.
    /// </summary>
    public const int EmbeddingDimensions = 768;

    public const int MaxEmbeddingModelLength = 100;

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int ChunkIndex { get; private set; }
    public int PageNumber { get; private set; }
    public string Text { get; private set; } = null!;

    /// <summary>
    /// Which model generated this chunk's embedding — recorded per-chunk, not assumed
    /// globally, so a future provider/model migration knows exactly what needs reindexing.
    /// </summary>
    public string EmbeddingModel { get; private set; } = null!;

    public float[] Embedding { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core
    private DocumentChunk()
    {
    }

    public DocumentChunk(
        Guid id,
        Guid documentId,
        int chunkIndex,
        int pageNumber,
        string text,
        string embeddingModel,
        float[] embedding,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id cannot be empty.", nameof(id));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId cannot be empty.", nameof(documentId));
        }

        if (chunkIndex < 0)
        {
            throw new ArgumentException("ChunkIndex cannot be negative.", nameof(chunkIndex));
        }

        if (pageNumber < 1)
        {
            throw new ArgumentException("PageNumber must be at least 1.", nameof(pageNumber));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be empty.", nameof(text));
        }

        if (string.IsNullOrWhiteSpace(embeddingModel))
        {
            throw new ArgumentException("EmbeddingModel cannot be empty.", nameof(embeddingModel));
        }

        if (embeddingModel.Length > MaxEmbeddingModelLength)
        {
            throw new ArgumentException(
                $"EmbeddingModel cannot exceed {MaxEmbeddingModelLength} characters.", nameof(embeddingModel));
        }

        if (embedding is null || embedding.Length != EmbeddingDimensions)
        {
            throw new ArgumentException($"Embedding must have exactly {EmbeddingDimensions} dimensions.", nameof(embedding));
        }

        Id = id;
        DocumentId = documentId;
        ChunkIndex = chunkIndex;
        PageNumber = pageNumber;
        Text = text;
        EmbeddingModel = embeddingModel;
        Embedding = embedding;
        CreatedAt = createdAt;
    }
}
