# ADR 0016 — Similarity search y endpoints RAG (`/api/search`, `/api/chat`)

**Status:** Aceptado

## Contexto

Última pieza de Fase 4: unir retrieval (`pgvector`) + `/generate` + citas en los endpoints
que finalmente exponen RAG — `PROJECT.md` §7:

```
Pregunta → Embedding(pregunta) → similarity search (pgvector) → Top-K chunks
        → construir contexto → LLM → Respuesta + citas
```

## Decisiones

**Sin índice de similaridad (`ivfflat`/`hnsw`) todavía.** A la escala de datos de un PoC
(decenas/cientos de chunks), un sequential scan exacto contra `pgvector` alcanza de sobra.
Tunear parámetros de índice (`lists`, `m`, `ef_construction`) sin volumen real para calibrar
sería adivinar — se agrega si/cuando el volumen de datos lo justifique (`ADR 0004` ya lo
anticipaba como "probable", no como decidido).

**`SimilaritySearchService` con SQL crudo, sin interfaz.** Usa el operador de distancia
coseno (`<=>`) de `pgvector` vía `Database.SqlQuery<T>` — es SQL específico de
Postgres/pgvector sin implementación alternativa posible, a diferencia de `IFileStorage`/
`IAiServiceClient` que sí son proveedores swappeables. No se abstrae por costumbre. Valida
la query en un test de integración con vectores sintéticos (direcciones ortogonales/iguales,
distancias predecibles exactas) antes de construir nada encima.

**Threshold configurable con valor de partida (`Rag:MaxRelevantDistance = 0.75`), no
calibrado empíricamente.** `PROJECT.md` §7 lo describe como "configurable" — se toma como
punto de partida razonable, explícitamente pendiente de ajuste con uso real. Si ningún chunk
recuperado pasa el threshold, .NET devuelve el fallback "insufficient context" **sin llamar
a `/generate`** — ahorra tiempo/costo y es la fuente primaria de esa garantía. La instrucción
del prompt del sistema en Python (ADR 0014) es la segunda capa, para el caso "hay algo
tópicamente similar pero no responde la pregunta puntual".

**`POST /api/search` (retrieval puro, sin LLM) y `POST /api/chat` (RAG completo)** como
endpoints separados — `PROJECT.md` §4.A los lista como cosas distintas, con casos de uso
distintos (explorar qué hay vs. obtener una respuesta).

**`GET /api/chat/{conversationId}` no valida `UserId` del caller** (solo que la conversación
exista) — consistente con que no hay auth todavía (ADR 0008); `POST /api/chat` sí valida que,
si se pasa un `conversationId` existente, pertenezca al `UserId` dado (devuelve 404 si no,
sin distinguir "no existe" de "es de otro usuario").

**Citas deduplicadas** — si varios chunks recuperados vienen del mismo archivo+página, la
lista de `Sources` los lista una sola vez (`Distinct()` sobre el record `Citation`, que tiene
igualdad por valor).

## Consecuencias

- 13 tests nuevos: 2 de `SimilaritySearchService` (SQL validado con vectores sintéticos antes
  de construir los endpoints encima), 3 de `/api/search`, 8 de `/api/chat`
  (incluyendo el caso "sin contexto relevante" y la persistencia real de `Conversation`/
  `Messages`). Total Fase 4: 96 tests.
- Verificado además con un smoke test manual completo de punta a punta: Api real + Worker
  real procesando un PDF real → `POST /api/chat` real preguntando sobre su contenido →
  respuesta correcta citando `sample.pdf — page 1` → `GET /api/chat/{id}` devolviendo el
  historial persistido tal cual.
- **Fase 4 (RAG) cerrada.** El pipeline completo — upload → procesamiento async → parse/
  chunk/embed → similarity search → generación con citas — funciona de punta a punta.
  Próximo: Fase 5 (Production polish) — que es donde entra la auth diferida (ADR 0008), el
  índice de similaridad si el volumen lo justifica, y el retry granular de `ProcessingJob`
  pendiente desde Fase 2.
