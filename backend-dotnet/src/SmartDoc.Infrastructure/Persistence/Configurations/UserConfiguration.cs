using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(User.MaxEmailLength);

        builder.HasIndex(u => u.Email)
            .IsUnique();

        // No max length: PasswordHasher<User>'s PBKDF2 output format could grow if its
        // default parameters change in a future version — unbounded text avoids coupling
        // the schema to today's exact hash length.
        builder.Property(u => u.PasswordHash)
            .IsRequired();

        builder.Property(u => u.CreatedAt)
            .IsRequired();
    }
}
