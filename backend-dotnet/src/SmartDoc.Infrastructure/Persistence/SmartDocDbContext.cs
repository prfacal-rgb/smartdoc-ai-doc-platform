using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Infrastructure.Persistence;

public class SmartDocDbContext : DbContext
{
    public SmartDocDbContext(DbContextOptions<SmartDocDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartDocDbContext).Assembly);
    }
}
