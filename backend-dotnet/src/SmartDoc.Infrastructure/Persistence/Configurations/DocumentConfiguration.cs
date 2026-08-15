using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.FileName)
            .IsRequired()
            .HasMaxLength(Document.MaxFileNameLength);

        builder.Property(d => d.ContentType)
            .IsRequired();

        builder.Property(d => d.StoragePath)
            .IsRequired()
            .HasMaxLength(Document.MaxStoragePathLength);

        builder.Property(d => d.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.Status);

        // No navigation properties on either entity by design (see ADR 0006) — User and
        // Document remain independent aggregates. Restrict rather than Cascade: User
        // deletion is meant to be logical, not physical (ADR 0006), so this only acts as a
        // safety net against an unintended physical delete taking Documents down with it.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
