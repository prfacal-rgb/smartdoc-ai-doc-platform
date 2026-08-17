using Microsoft.EntityFrameworkCore;
using Pgvector;
using SmartDoc.Infrastructure.Persistence;

namespace SmartDoc.Infrastructure.Search;

public sealed record SimilarChunk(
    Guid ChunkId, Guid DocumentId, string FileName, int PageNumber, string Text, double Distance);

/// <summary>
/// Cosine-distance nearest-neighbor search against DocumentChunks.Embedding, via raw SQL
/// (ADR 0004 anticipated this). Backed by an HNSW index (vector_cosine_ops, matching the
/// `<=>` operator below) since ADR 0019 — the query itself is unchanged by that; the planner
/// picks the index automatically once row counts make it worthwhile, seq-scanning small
/// tables regardless (that's expected, not a bug — see ADR 0019 for how it was verified). Not
/// abstracted behind an interface — this is Postgres/pgvector-specific SQL with no swappable
/// implementation, unlike the genuinely pluggable providers (IFileStorage, IAiServiceClient).
/// </summary>
public class SimilaritySearchService(SmartDocDbContext db)
{
    public async Task<IReadOnlyList<SimilarChunk>> SearchAsync(
        float[] queryEmbedding, int topK, CancellationToken cancellationToken = default)
    {
        var queryVector = new Vector(queryEmbedding);

        var rows = await db.Database.SqlQuery<SimilarChunkRow>($"""
            SELECT dc."Id" AS "ChunkId", dc."DocumentId", d."FileName", dc."PageNumber", dc."Text",
                   dc."Embedding" <=> {queryVector} AS "Distance"
            FROM "DocumentChunks" dc
            JOIN "Documents" d ON d."Id" = dc."DocumentId"
            ORDER BY dc."Embedding" <=> {queryVector}
            LIMIT {topK}
            """).ToListAsync(cancellationToken);

        return rows
            .Select(r => new SimilarChunk(r.ChunkId, r.DocumentId, r.FileName, r.PageNumber, r.Text, r.Distance))
            .ToList();
    }

    private sealed record SimilarChunkRow(Guid ChunkId, Guid DocumentId, string FileName, int PageNumber, string Text, double Distance);
}
