# ADR 0019 — Índice HNSW sobre `DocumentChunks.Embedding`

**Status:** Aceptado

## Contexto

ADR 0016 había dejado explícitamente sin índice de similaridad (`ivfflat`/`hnsw`) el
sequential scan contra `pgvector`, con la condición de agregarlo "si/cuando el volumen de
datos lo justifique". A la fecha de esta decisión, `DocumentChunks` sigue en **0 filas** en
este entorno (los tests de integración limpian todo lo que crean) — no hay volumen real de
producción para calibrar contra. Se documenta esto sin rodeos: la decisión de agregar el
índice ahora es una demostración deliberada de criterio de arquitectura para portfolio, no
una optimización motivada por un cuello de botella medido.

## Decisiones

**HNSW, no `ivfflat`.** La razón no es solo "HNSW suele dar mejor recall/latencia en
`pgvector`" en general — hay un motivo concreto para *este* proyecto: `ivfflat` calcula sus
clusters (k-means sobre las `lists`) a partir de los datos presentes en la tabla en el
momento de `CREATE INDEX`. Como `DocumentChunks` arranca vacía y crece de a un documento
subido por vez, construir un `ivfflat` hoy generaría clusters degenerados que nunca se
recalculan solos (requeriría un `REINDEX` manual más adelante). HNSW construye su grafo de
forma incremental a medida que se insertan filas — no depende de que haya datos
representativos al momento de crear el índice, que es exactamente la situación de este
proyecto.

**`vector_cosine_ops`, no L2/inner product.** Tiene que matchear el operador de distancia que
ya usa `SimilaritySearchService` (`<=>`, coseno) desde ADR 0016 — un índice con la clase de
operador equivocada simplemente no se usa para esa query, sin error visible, un error fácil
de cometer en silencio.

**`m`/`ef_construction` en los defaults de pgvector (16/64), sin tunear.** Mismo criterio que
`Rag:MaxRelevantDistance` en ADR 0016: tunear estos parámetros sin patrones de uso reales para
calibrar contra sería adivinar. Los defaults de la librería son un punto de partida razonable
y documentado como tal — no un valor elegido con intención específica para este dataset.

**Migration vía EF Core (`.HasMethod("hnsw").HasOperators("vector_cosine_ops")` en
`DocumentChunkConfiguration`), no SQL crudo fuera del flujo de migrations.** Consistente con
ADR 0003/0004 (migrations automáticas, Code-First) — el índice queda versionado junto con el
resto del schema, no como un paso manual documentado aparte.

## Verificación

Con la tabla vacía, `EXPLAIN` sobre la query de `SimilaritySearchService` da (correctamente)
un `Seq Scan` — Postgres no usa un índice sobre una tabla con pocas filas, sea cual sea el
tipo de índice; eso **no** es evidencia de que el índice esté mal configurado. Para verificar
de verdad que el índice se usa, se cargaron temporalmente 5000 filas sintéticas (vectores
aleatorios de 768 dimensiones, generados en SQL) y se corrió `EXPLAIN (ANALYZE, BUFFERS)` con
la query real:

- **Antes de `ANALYZE`:** el planner subestimó las filas (estadísticas desactualizadas tras
  el bulk insert) y eligió un `Bitmap Index Scan` sobre el índice de `DocumentId` + sort
  manual, ignorando el HNSW — un hallazgo real en sí mismo: un `INSERT` masivo sin `ANALYZE`
  posterior puede llevar al planner a ignorar un índice bien construido. En producción,
  autovacuum/autoanalyze de Postgres se encarga de esto solo a medida que se acumulan
  escrituras; acá se corrió manual porque la carga fue toda de una vez.
- **Después de `ANALYZE "DocumentChunks"`:** el plan pasó a `Index Scan using
  "IX_DocumentChunks_Embedding" ... Order By: ("Embedding" <=> $0)` — el índice HNSW siendo
  usado exactamente para el patrón `ORDER BY embedding <=> :query LIMIT :topK` que
  `SimilaritySearchService` ejecuta.

Datos sintéticos borrados (cascade vía el `Document` que los contenía) al terminar — el
entorno queda igual que antes de la verificación.

## Consecuencias

- Migration `AddDocumentChunksEmbeddingHnswIndex`: `CREATE INDEX ... USING hnsw ("Embedding"
  vector_cosine_ops)`, aplicada contra Postgres.
- `SimilaritySearchService` no cambia — la query SQL es la misma; el índice es transparente
  para el código que la ejecuta, el planner decide solo cuándo usarlo.
- 116 tests sin cambios (el índice no altera resultados, solo el plan de ejecución) —
  confirmado que la suite completa sigue pasando después de agregar la migration.
- Pendiente, explícitamente fuera de este ADR: tunear `m`/`ef_construction` (build-time) o
  `hnsw.ef_search` (query-time, sesión) requiere patrones de uso reales — candidato futuro
  igual que el ajuste de `Rag:MaxRelevantDistance` (ADR 0016), no antes de tener tráfico real
  para medir contra.
