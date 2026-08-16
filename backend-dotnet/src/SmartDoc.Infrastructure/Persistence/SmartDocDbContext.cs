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
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartDocDbContext).Assembly);
    }
}
