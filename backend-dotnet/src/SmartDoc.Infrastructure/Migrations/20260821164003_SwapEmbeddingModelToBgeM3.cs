using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace SmartDoc.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Hand-edited after scaffolding (ADR 0026): the generated AlterColumn alone doesn't work
    // — Postgres can't reinterpret an existing vector(768) row as vector(1024) (different
    // dimensions aren't a castable pair), and it can't alter a column an HNSW index depends
    // on while that index still exists. Since every existing chunk's embedding is from the
    // model being replaced anyway (nomic-embed-text -> bge-m3), there's nothing worth
    // preserving: drop the index, clear the table, resize the column, rebuild the index on
    // the now-empty table (same HNSW-over-ivfflat reasoning as ADR 0019 — it builds
    // incrementally, so an empty table is the expected starting point, not a problem).
    // Reprocessing every Document (outside this migration, see ADR 0026) repopulates the
    // table with 1024-dim embeddings from the new model.
    public partial class SwapEmbeddingModelToBgeM3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_Embedding",
                table: "DocumentChunks");

            migrationBuilder.Sql("DELETE FROM \"DocumentChunks\";");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "DocumentChunks",
                type: "vector(1024)",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector(768)");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_Embedding",
                table: "DocumentChunks",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_Embedding",
                table: "DocumentChunks");

            migrationBuilder.Sql("DELETE FROM \"DocumentChunks\";");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "DocumentChunks",
                type: "vector(768)",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector(1024)");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_Embedding",
                table: "DocumentChunks",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }
    }
}
