using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.HasIndex(c => c.UserId);

        // Restrict, same reasoning as Document.UserId -> Users.Id (ADR 0006): User has
        // independent audit value, borrado is logical not physical.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
