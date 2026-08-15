# ADR 0013 — `DocumentChunks` y wiring real del `ProcessingJobProcessor`

**Status:** Aceptado

## Contexto

Última pieza de Fase 3: reemplazar el placeholder de Fase 2 (`Task.Delay` + transición de
estado) por el flujo real — leer el archivo de MinIO, llamar a `ai-service-python`
(`/parse` → `/chunk` → `/embed`), y persistir los `DocumentChunks` resultantes.

## Decisiones

**`DocumentChunk` guarda `Embedding` como `float[]` en el dominio, no `Pgvector.Vector`.**
`SmartDoc.Domain` no puede depender de tipos de infraestructura (ADR 0005: "Domain permanece
libre de dependencias de infraestructura"). `Pgvector.Vector`, aunque técnicamente
provider-agnostic en sí mismo, sigue siendo un tipo de una librería de persistencia. La
conversión `float[] ↔ Vector` vive únicamente en `DocumentChunkConfiguration` vía
`HasConversion`, mismo patrón que `DocumentStatus ↔ string` (ADR previo) — el dominio no sabe
cómo se representa en la base.

**Dimensión y modelo como invariantes de dominio, no solo de schema.** El constructor de
`DocumentChunk` valida `embedding.Length == EmbeddingDimensions` (768, constante compartida
entre dominio y configuración EF, mismo patrón que `Document.MaxFileNameLength`) — un mismatch
de dimensión falla con un `ArgumentException` claro al construir la entidad, en vez de un
error crudo de Postgres al hacer `SaveChanges`. `EmbeddingModel` es obligatorio y no puede
estar vacío.

**`ValueComparer` explícito para `Embedding`.** EF Core avisó (warning, no error) que un
value converter sobre un tipo colección sin `ValueComparer` compara por referencia, no por
valor — se agregó un comparer basado en `SequenceEqual` antes de generar la migration, no
después.

**Falta explícita: `CREATE EXTENSION vector`.** `Pgvector.EntityFrameworkCore`/`UseVector()`
solo agregan el mapeo de tipos .NET ↔ Postgres; la extensión de Postgres en sí no se habilita
sola (la imagen `pgvector/pgvector:pg16` la trae compilada, no activada). Se agregó
`migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;")` al inicio de la migration
`AddDocumentChunks` — encontrado porque la primera corrida de `dotnet ef database update`
falló con `type "vector" does not exist`.

**`IAiServiceClient` como puerto en `SmartDoc.Application`**, implementado en
`SmartDoc.Infrastructure` con `HttpClient` (`AddHttpClient` tipado). JSON en snake_case
(`JsonNamingPolicy.SnakeCaseLower`) para calzar con la serialización default de
FastAPI/pydantic, sin decorar cada DTO con `[JsonPropertyName]`.

**`ProcessingJobProcessor` ahora hace el trabajo real**: lee el archivo vía `IFileStorage`,
`/parse` → `/chunk` → `/embed`, arma un `DocumentChunk` por cada chunk (`Zip` de chunks +
embeddings por índice), y falla el job completo si cualquier paso falla — sin
éxito/reintentos parciales todavía (mismo criterio que ADR 0009: retry granular queda para
cuando haga falta de verdad).

## Consecuencias

- Migration `AddDocumentChunks` aplicada (incluye `CREATE EXTENSION vector`).
- 12 tests nuevos: unit tests de `DocumentChunk` (validación, incluida la dimensión),
  integration tests de persistencia (round-trip del embedding, unique constraint
  `(DocumentId, ChunkIndex)`, FK rejection, cascade delete), y un test end-to-end del
  `ProcessingJobProcessor` contra el stack real completo (Postgres + MinIO + ai-service +
  Ollama) usando un PDF de fixture real (generado con `fpdf2`, checkeado como binario de
  test — mismo criterio que la Fase 2, priorizar tests reales sobre mocks contra la
  infraestructura que ya existe).
- **Verificado además con un smoke test manual real**, no solo tests: `POST
  /api/documents` (Api real) → Worker real corriendo su polling loop → `DocumentChunks`
  persistido con `EmbeddingModel = nomic-embed-text`, `Document: Ready`. Limpieza manual
  incluyó borrar el objeto huérfano de MinIO con un contenedor `minio/mc` descartable
  (`docker exec`/`curl` no alcanzan para operaciones de objeto S3 sin un cliente S3).
- **Fase 3 cerrada.** Pipeline completo PDF → texto → chunks → embeddings → `DocumentChunks`
  funcionando de punta a punta. Próximo: Fase 4 (RAG) — similarity search contra `pgvector`
  desde .NET, construcción de contexto, `/generate` en Python (recién ahí se decide LLM
  provider), y citas con página.
