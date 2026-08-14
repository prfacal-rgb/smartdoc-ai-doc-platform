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
            .HasMaxLength(260);

        builder.Property(d => d.ContentType)
            .IsRequired();

        builder.Property(d => d.StoragePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(d => d.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.Status);
    }
}
