using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Infrastructure.Persistence.Configurations;

public class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(j => j.RetryCount)
            .IsRequired();

        builder.Property(j => j.ErrorMessage)
            .HasMaxLength(ProcessingJob.MaxErrorMessageLength);

        builder.Property(j => j.CreatedAt)
            .IsRequired();

        builder.Property(j => j.UpdatedAt)
            .IsRequired();

        builder.HasIndex(j => j.DocumentId);
        builder.HasIndex(j => j.Status);

        // Cascade, unlike Document -> User (ADR 0006): a ProcessingJob has no meaning
        // without its Document, so deleting the Document should take its jobs with it.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(j => j.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
