using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;

namespace SmartDoc.IntegrationTests.Persistence;

/// <summary>
/// Exercises DocumentChunk persistence against real Postgres/pgvector, including the FK to
/// Document (cascade delete, same as ProcessingJob) and the unique (DocumentId, ChunkIndex)
/// constraint.
/// </summary>
public class DocumentChunkPersistenceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public DocumentChunkPersistenceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static float[] SampleEmbedding()
    {
        var embedding = new float[DocumentChunk.EmbeddingDimensions];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = i / (float)embedding.Length;
        }

        return embedding;
    }

    [Fact]
    public async Task SaveChanges_PersistsDocumentChunk_RoundTripsEmbeddingAndMetadata()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);
        var embedding = SampleEmbedding();
        var chunk = new DocumentChunk(Guid.NewGuid(), document.Id, 0, 1, "chunk text", "nomic-embed-text", embedding, DateTimeOffset.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Documents.Add(document);
            writeContext.DocumentChunks.Add(chunk);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            await using var readContext = _fixture.CreateContext();
            var reloaded = await readContext.DocumentChunks.SingleAsync(c => c.Id == chunk.Id);

            reloaded.DocumentId.Should().Be(document.Id);
            reloaded.ChunkIndex.Should().Be(0);
            reloaded.PageNumber.Should().Be(1);
            reloaded.Text.Should().Be("chunk text");
            reloaded.EmbeddingModel.Should().Be("nomic-embed-text");
            reloaded.Embedding.Should().HaveCount(DocumentChunk.EmbeddingDimensions);
            reloaded.Embedding.Should().BeEquivalentTo(embedding, options => options.WithStrictOrdering());
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Documents.Remove(document); // cascades the chunk
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_DuplicateChunkIndexForSameDocument_ThrowsDbUpdateException()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);
        var chunk1 = new DocumentChunk(Guid.NewGuid(), document.Id, 0, 1, "first", "nomic-embed-text", SampleEmbedding(), DateTimeOffset.UtcNow);
        var chunk2 = new DocumentChunk(Guid.NewGuid(), document.Id, 0, 1, "second", "nomic-embed-text", SampleEmbedding(), DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.Users.Add(user);
        context.Documents.Add(document);
        context.DocumentChunks.Add(chunk1);
        await context.SaveChangesAsync();

        try
        {
            context.DocumentChunks.Add(chunk2);
            var act = async () => await context.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            context.Entry(chunk2).State = EntityState.Detached;
            context.Documents.Remove(document);
            context.Users.Remove(user);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_DocumentChunkWithNonExistentDocumentId_ThrowsDbUpdateException()
    {
        var chunk = new DocumentChunk(Guid.NewGuid(), Guid.NewGuid(), 0, 1, "text", "nomic-embed-text", SampleEmbedding(), DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateContext();
        context.DocumentChunks.Add(chunk);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingDocument_CascadeDeletesItsChunks()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);
        var chunk = new DocumentChunk(Guid.NewGuid(), document.Id, 0, 1, "text", "nomic-embed-text", SampleEmbedding(), DateTimeOffset.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Documents.Add(document);
            writeContext.DocumentChunks.Add(chunk);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            await using (var deleteContext = _fixture.CreateContext())
            {
                var trackedDocument = await deleteContext.Documents.SingleAsync(d => d.Id == document.Id);
                deleteContext.Documents.Remove(trackedDocument);
                await deleteContext.SaveChangesAsync();
            }

            await using var verifyContext = _fixture.CreateContext();
            var chunkStillExists = await verifyContext.DocumentChunks.AnyAsync(c => c.Id == chunk.Id);
            chunkStillExists.Should().BeFalse();
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }
}
