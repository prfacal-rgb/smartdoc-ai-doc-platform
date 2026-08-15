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

        // Cascade, same reasoning as ProcessingJob -> Document (ADR 0009): a chunk has no
        // meaning without its document. No similarity index yet (ivfflat/hnsw) — nothing
        // queries by similarity until Phase 4 (RAG); see ADR 0004.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
