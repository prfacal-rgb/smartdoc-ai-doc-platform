using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Infrastructure.Persistence.Configurations;

public class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Text)
            .IsRequired();

        builder.Property(c => c.EmbeddingModel)
            .IsRequired()
            .HasMaxLength(DocumentChunk.MaxEmbeddingModelLength);

        // Fixed dimension (ADR 0010/0012) — vector(N) in Postgres has no notion of a
        // variable-length vector; the column type itself encodes the dimension.
        var embeddingComparer = new ValueComparer<float[]>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
            v => v.ToArray());

        builder.Property(c => c.Embedding)
            .HasConversion(v => new Vector(v), v => v.ToArray())
            .HasColumnType($"vector({DocumentChunk.EmbeddingDimensions})")
            .IsRequired()
            .Metadata.SetValueComparer(embeddingComparer);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.HasIndex(c => c.DocumentId);
        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex }).IsUnique();

        // HNSW over ivfflat: builds incrementally as rows are inserted, so it doesn't need
        // representative data present at CREATE INDEX time the way ivfflat's k-means-based
        // list clustering does (a real pitfall here — this table starts empty and grows one
        // document at a time). vector_cosine_ops matches the <=> operator SimilaritySearchService
        // already queries with (ADR 0016). m/ef_construction left at pgvector's defaults —
        // no real query volume yet to tune them against. See ADR 0019.
        builder.HasIndex(c => c.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops");

        // Cascade, same reasoning as ProcessingJob -> Document (ADR 0009): a chunk has no
        // meaning without its document.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
