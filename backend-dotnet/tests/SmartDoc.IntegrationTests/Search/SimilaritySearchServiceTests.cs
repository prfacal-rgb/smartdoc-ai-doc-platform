using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;
using SmartDoc.Infrastructure.Search;
using SmartDoc.IntegrationTests.Persistence;

namespace SmartDoc.IntegrationTests.Search;

/// <summary>
/// Validates the raw SQL cosine-distance query (SimilaritySearchService) against real
/// Postgres/pgvector before anything is built on top of it.
/// </summary>
public class SimilaritySearchServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public SimilaritySearchServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Builds a unit vector pointing mostly along one axis, for predictable distances.</summary>
    private static float[] AxisVector(int axis)
    {
        var vector = new float[DocumentChunk.EmbeddingDimensions];
        vector[axis] = 1f;
        return vector;
    }

    [Fact]
    public async Task SearchAsync_ReturnsChunksOrderedByAscendingDistance()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);

        // chunkClose has the exact same direction as the query vector (axis 0) -> distance ~0.
        // chunkFar points along a different axis (axis 1) -> orthogonal -> distance ~1.
        var chunkClose = new DocumentChunk(
            Guid.NewGuid(), document.Id, 0, 1, "closely related text", "nomic-embed-text", AxisVector(0), DateTimeOffset.UtcNow);
        var chunkFar = new DocumentChunk(
            Guid.NewGuid(), document.Id, 1, 1, "unrelated text", "nomic-embed-text", AxisVector(1), DateTimeOffset.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Documents.Add(document);
            writeContext.DocumentChunks.AddRange(chunkClose, chunkFar);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            await using var readContext = _fixture.CreateContext();
            var service = new SimilaritySearchService(readContext);

            // topK covers every row in the table, not just this test's own 2 - DocumentChunks
            // is never test-isolated (Documents is a shared knowledge base by design, ADR
            // 0017) and, since ADR 0022, the dev DB legitimately carries a real calibration
            // corpus alongside whatever a test inserts. Filtering to this test's own
            // document.Id below is what makes the assertion correct regardless of what else
            // is in there - a fixed topK: 5 would silently break the moment enough unrelated
            // chunks rank closer to AxisVector(0) than the orthogonal chunkFar does.
            var totalChunks = await readContext.DocumentChunks.CountAsync();
            var results = await service.SearchAsync(AxisVector(0), topK: totalChunks);
            var ownResults = results.Where(r => r.DocumentId == document.Id).ToList();

            ownResults.Should().HaveCount(2);
            ownResults[0].ChunkId.Should().Be(chunkClose.Id);
            ownResults[0].Distance.Should().BeApproximately(0, 0.0001);
            ownResults[0].FileName.Should().Be("report.pdf");
            ownResults[0].PageNumber.Should().Be(1);
            ownResults[0].Text.Should().Be("closely related text");

            ownResults[1].ChunkId.Should().Be(chunkFar.Id);
            ownResults[1].Distance.Should().BeApproximately(1, 0.0001);
            ownResults[0].Distance.Should().BeLessThan(ownResults[1].Distance);
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Documents.Remove(document); // cascades the chunks
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SearchAsync_RespectsTopKLimit()
    {
        var user = new User(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", "test-password-hash", DateTimeOffset.UtcNow);
        var document = new Document(
            Guid.NewGuid(), user.Id, "report.pdf", "application/pdf", $"/storage/{Guid.NewGuid():N}.pdf", DateTimeOffset.UtcNow);
        var chunks = Enumerable.Range(0, 5)
            .Select(i => new DocumentChunk(
                Guid.NewGuid(), document.Id, i, 1, $"chunk {i}", "nomic-embed-text", AxisVector(i), DateTimeOffset.UtcNow))
            .ToList();

        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Users.Add(user);
            writeContext.Documents.Add(document);
            writeContext.DocumentChunks.AddRange(chunks);
            await writeContext.SaveChangesAsync();
        }

        try
        {
            await using var readContext = _fixture.CreateContext();
            var service = new SimilaritySearchService(readContext);

            var results = await service.SearchAsync(AxisVector(0), topK: 2);

            results.Should().HaveCount(2);
        }
        finally
        {
            await using var cleanupContext = _fixture.CreateContext();
            cleanupContext.Documents.Remove(document);
            cleanupContext.Users.Remove(user);
            await cleanupContext.SaveChangesAsync();
        }
    }
}
