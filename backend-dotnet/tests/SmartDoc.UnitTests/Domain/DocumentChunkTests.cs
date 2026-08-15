using FluentAssertions;
using SmartDoc.Domain.Entities;

namespace SmartDoc.UnitTests.Domain;

public class DocumentChunkTests
{
    private static float[] ValidEmbedding() => new float[DocumentChunk.EmbeddingDimensions];

    [Fact]
    public void Constructor_WithValidData_SetsProperties()
    {
        var id = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var embedding = ValidEmbedding();
        var createdAt = DateTimeOffset.UtcNow;

        var chunk = new DocumentChunk(id, documentId, 0, 1, "some chunk text", "nomic-embed-text", embedding, createdAt);

        chunk.Id.Should().Be(id);
        chunk.DocumentId.Should().Be(documentId);
        chunk.ChunkIndex.Should().Be(0);
        chunk.PageNumber.Should().Be(1);
        chunk.Text.Should().Be("some chunk text");
        chunk.EmbeddingModel.Should().Be("nomic-embed-text");
        chunk.Embedding.Should().BeSameAs(embedding);
        chunk.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        var act = () => new DocumentChunk(
            Guid.Empty, Guid.NewGuid(), 0, 1, "text", "nomic-embed-text", ValidEmbedding(), DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public void Constructor_WithEmptyDocumentId_Throws()
    {
        var act = () => new DocumentChunk(
            Guid.NewGuid(), Guid.Empty, 0, 1, "text", "nomic-embed-text", ValidEmbedding(), DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("documentId");
    }

    [Fact]
    public void Constructor_WithNegativeChunkIndex_Throws()
    {
        var act = () => new DocumentChunk(
            Guid.NewGuid(), Guid.NewGuid(), -1, 1, "text", "nomic-embed-text", ValidEmbedding(), DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("chunkIndex");
    }

    [Fact]
    public void Constructor_WithPageNumberLessThanOne_Throws()
    {
        var act = () => new DocumentChunk(
            Guid.NewGuid(), Guid.NewGuid(), 0, 0, "text", "nomic-embed-text", ValidEmbedding(), DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("pageNumber");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyText_Throws(string text)
    {
        var act = () => new DocumentChunk(
            Guid.NewGuid(), Guid.NewGuid(), 0, 1, text, "nomic-embed-text", ValidEmbedding(), DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("text");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyEmbeddingModel_Throws(string model)
    {
        var act = () => new DocumentChunk(
            Guid.NewGuid(), Guid.NewGuid(), 0, 1, "text", model, ValidEmbedding(), DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("embeddingModel");
    }

    [Fact]
    public void Constructor_WithEmbeddingModelExceedingMaxLength_Throws()
    {
        var tooLongModel = new string('a', DocumentChunk.MaxEmbeddingModelLength + 1);

        var act = () => new DocumentChunk(
            Guid.NewGuid(), Guid.NewGuid(), 0, 1, "text", tooLongModel, ValidEmbedding(), DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("embeddingModel");
    }

    [Fact]
    public void Constructor_WithNullEmbedding_Throws()
    {
        var act = () => new DocumentChunk(
            Guid.NewGuid(), Guid.NewGuid(), 0, 1, "text", "nomic-embed-text", null!, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("embedding");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(767)]
    [InlineData(769)]
    [InlineData(1536)]
    public void Constructor_WithWrongEmbeddingDimensions_Throws(int dimensions)
    {
        var act = () => new DocumentChunk(
            Guid.NewGuid(), Guid.NewGuid(), 0, 1, "text", "nomic-embed-text", new float[dimensions], DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("embedding");
    }
}
