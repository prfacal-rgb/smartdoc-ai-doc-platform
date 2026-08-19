# CLAUDE.md

Este archivo guía a Claude Code en el desarrollo de **SmartDoc** (AI Document Intelligence
Platform). Léelo antes de generar código. El detalle completo de arquitectura y decisiones
está en `PROJECT.md` — este archivo es la guía operativa de desarrollo. Decisiones puntuales
no triviales quedan documentadas como ADRs en `docs/decisions/`.

## Entorno de desarrollo

Desarrollo (y probablemente ejecución de la PoC) en la VM Windows 11 de Pablo (sobre VMware
Workstation) — distinta de su VM Debian 13 usada para otros proyectos con Claude Code.

SDK de .NET: **10.0.400**, confirmado instalado y fijado vía `global.json` en la raíz de
`backend-dotnet/` (ver ADR 0001). Convive sin problema con el 8.0.419 preexistente.

Docker Desktop (con backend WSL2) está operativo y es el mecanismo de orquestación tanto en
desarrollo como referencia de despliegue — ver ADR 0002 para el detalle de la resolución
(virtualización anidada en VMware Workstation).

## Sobre este proyecto

Proyecto de portfolio (PoC), no producto comercial. El objetivo prioritario es calidad de
arquitectura y claridad de decisiones por sobre cantidad de features. Ante la duda entre
"agregar algo más" y "mantenerlo simple y bien hecho", elegir simple y bien hecho.

## Stack

- **Backend principal**: .NET 10, ASP.NET Core, Minimal APIs, Vertical Slice Architecture,
  EF Core (Code-First, migrations automáticas — ver ADR 0003 y ADR 0004).
- **AI Service**: Python 3.12+, FastAPI.
- **Base de datos**: PostgreSQL + pgvector, vía Docker Compose (imagen
  `pgvector/pgvector:pg16`).
- **Storage de archivos**: object storage (local/MinIO en dev; definir equivalente cloud
  más adelante si aplica).
- **Auth**: JWT, seed user único (sin registro público en MVP).
- **Logging**: Serilog (.NET), logging estructurado equivalente en Python.
- **Testing**: xUnit + FluentAssertions (.NET), pytest (Python).
- **Orquestación local**: Docker Compose (`docker-compose.yml` en la raíz del repo).
- **LLM/Embeddings**: abstraídos detrás de una interfaz (`ILlmProvider` / `IEmbeddingProvider`).
  Implementación inicial recomendada: Anthropic (chat) + Ollama local (embeddings). No
  hardcodear el proveedor en la lógica de negocio.

## Estructura del repo (mono-repo)

```
smartdoc/
├── CLAUDE.md
├── PROJECT.md
├── docker-compose.yml
├── .env.example
├── backend-dotnet/
│   ├── global.json
│   ├── SmartDoc.sln
│   ├── src/
│   │   ├── SmartDoc.Api/
│   │   ├── SmartDoc.Application/
│   │   ├── SmartDoc.Domain/
│   │   ├── SmartDoc.Infrastructure/
│   │   └── SmartDoc.Worker/
│   └── tests/
│       ├── SmartDoc.UnitTests/
│       └── SmartDoc.IntegrationTests/
├── ai-service-python/
│   ├── app/
│   │   ├── main.py
│   │   ├── parsing/
│   │   ├── chunking/
│   │   ├── embeddings/
│   │   └── llm/
│   └── tests/
└── docs/
    ├── architecture.md
    └── decisions/          # ADRs, una por decisión relevante
```

`frontend/` se agrega recién en Fase 6, cuando el backend tenga funcionalidad básica
estable — no crear la carpeta antes de eso.

## Fases de desarrollo (seguir en orden, no saltar)

1. **Backend foundation** — API .NET + EF Core + PostgreSQL. CRUD de Documents, Users,
   estados. Sin AI todavía. *(cerrada — ver "Estado actual")*
2. **Async processing** — Job/Worker pattern. Upload devuelve `202 Accepted` sin esperar
   procesamiento. *(cerrada — ver "Estado actual")*
3. **AI pipeline** — Servicio Python (parse/chunk/embed) + integración con .NET Worker.
   *(cerrada — ver "Estado actual")*
4. **RAG** — Retrieval + construcción de contexto + generación + citas.
   *(cerrada — ver "Estado actual")*
5. **Production polish** — Tests, logs, Docker Compose completo, manejo de errores,
   documentación. No agregar features nuevas en esta fase.
6. **Frontend** (post-MVP) — a evaluar tecnología cuando llegue el momento.

No avanzar de fase sin que la anterior tenga tests pasando.

## Contrato .NET ↔ Python

- Python es **stateless**: no accede a la base de datos directamente.
- `.NET` hace la similarity search contra `pgvector` (no Python).
- `.NET` llama a `Python /embed` para vectorizar preguntas/documentos, y a
  `Python /generate` para la respuesta final del LLM con contexto ya armado.
- Comunicación HTTP interna (no exponer el servicio Python públicamente).

## Convenciones de código

### .NET / C#
- Nullable reference types habilitado.
- Minimal APIs — un endpoint = un caso de uso (vertical slice), sin controllers gordos
  (ver ADR 0003).
- Repositorios/servicios detrás de interfaces cuando haya una razón concreta de
  testabilidad o swap de implementación (no abstraer por costumbre).
- FluentValidation para validación de entrada.
- No exponer entidades de EF Core directamente en las respuestas de API — usar DTOs.

### Python
- Type hints obligatorios.
- Pydantic para request/response models de FastAPI.
- Un router por operación (`parse`, `chunk`, `embed`, `generate`).

### General
- Commits en inglés, mensajes descriptivos (convención conventional commits si es posible).
- Cada decisión arquitectónica no trivial → un ADR corto en `docs/decisions/`.
- README del repo en inglés (es portfolio, apunta a reclutadores/empresas internacionales).

## Variables de entorno (esperadas)

```
# .NET API
ConnectionStrings__Postgres=
Jwt__Secret=
Jwt__SeedUserEmail=
Jwt__SeedUserPassword=

# MinIO (object storage — usado por docker-compose.yml y Minio:* en appsettings)
MINIO_ROOT_USER=
MINIO_ROOT_PASSWORD=
MINIO_BUCKET_NAME=

# Proveedores AI (abstraídos — completar según implementación elegida)
LLM_PROVIDER=anthropic|openai|ollama
EMBEDDING_PROVIDER=ollama|openai
ANTHROPIC_API_KEY=
OPENAI_API_KEY=
OLLAMA_BASE_URL=  # embeddings: nomic-embed-text (768 dim) en Ollama corriendo en la
                   # máquina física, no en la VM ni en Docker.

# Python AI Service
AI_SERVICE_PORT=

# Docker Compose (ver .env.example en la raíz del repo)
POSTGRES_USER=
POSTGRES_PASSWORD=
POSTGRES_DB=
```

## Comandos de desarrollo

```bash
# Levantar todo (Postgres + servicios)
docker compose up -d

# Backend .NET
cd backend-dotnet
dotnet restore
dotnet ef database update --project src/SmartDoc.Infrastructure
dotnet test
dotnet run --project src/SmartDoc.Api

# AI Service Python
cd ai-service-python
pip install -r requirements.txt
pytest
uvicorn app.main:app --reload
```

## Qué NO hacer (scope guard)

No agregar sin que se pida explícitamente:
- Microservicios adicionales, Kubernetes, Kafka/RabbitMQ.
- Multi-tenancy, billing, registro público de usuarios.
- Más de un tipo de archivo soportado (solo PDF en MVP).
- Múltiples proveedores de LLM activos simultáneamente (la abstracción existe; la
  integración concurrente de varios, no).
- Frontend antes de Fase 6.

Si en algún punto del desarrollo aparece la tentación de agregar algo de esta lista,
señalarlo explícitamente en la respuesta y preguntar antes de implementar.

## Estado actual

**Fases 1-4 cerradas (Backend foundation, Async processing, AI pipeline, RAG). Fase 5
(Production polish) en progreso — Auth (JWT) implementado.**

Completado:
- Entorno de desarrollo operativo: Docker Desktop + WSL2, `docker compose up -d` levanta
  PostgreSQL+pgvector (`smartdoc-postgres`, healthy).
- Solución .NET scaffoldeada: 7 proyectos (`Api`, `Application`, `Domain`, `Infrastructure`,
  `Worker`, `UnitTests`, `IntegrationTests`), referencias entre capas configuradas según
  ADR 0005, `dotnet build` exitoso.
- SDK fijado vía `global.json` (10.0.400).
- ADRs 0001–0005 documentados en `docs/decisions/`.
- `SmartDocDbContext` (`SmartDoc.Infrastructure`) + entidades `User` y `Document`
  (`SmartDoc.Domain`), con configuraciones EF Core (`Email` único, longitudes máximas,
  `Status` persistido como string) y migration `InitialCreate` aplicada contra Postgres.
- `AddInfrastructure()` (DI) registrado en `SmartDoc.Api/Program.cs`, con soporte de
  pgvector habilitado (`UseVector()`) desde ya aunque todavía no haya columnas `vector`.
- Unit tests de `User`/`Document` (validación de constructor, longitudes máximas,
  transiciones de estado) e integration tests de `SmartDocDbContext` contra Postgres real
  (unique constraint de `Email`, persistencia de `Status` como texto legible, FK
  `Document.UserId` → `Users.Id`).
- Foreign Key `Document.UserId` → `Users.Id` (`DeleteBehavior.Restrict`, sin navigation
  properties) — ver ADR 0006. Borrado de `User` decidido como lógico (no físico), pero su
  implementación (`DeletedAt`/`SoftDelete()`) queda diferida hasta que exista un endpoint
  real de borrado de usuario.
- CRUD de `Documents` (`POST`/`GET`/`GET {id}`/`DELETE`) en `SmartDoc.Api/Features/Documents/`
  — handlers Minimal API con `SmartDocDbContext` inyectado directamente (sin repositorio;
  ver ADR 0007), FluentValidation, DTOs propios (nunca se exponen entidades EF Core). Solo
  metadata en esta fase, sin archivo real (no hay object storage provisionado todavía).
- Seed user mínimo al arrancar la Api (`SmartDocDbContextSeeder`, no es auth real) para que
  `Document.UserId` tenga un valor válido con el que probar — ver ADR 0007.
- OpenAPI nativo (`Microsoft.AspNetCore.OpenApi`) + Scalar UI en `/scalar/v1` (dev only) —
  ver ADR 0007 para el porqué de Scalar en vez de Swashbuckle (conflicto real de versión de
  `Microsoft.OpenApi` entre ambos).
- 8 integration tests end-to-end de los endpoints de `Documents` vía
  `WebApplicationFactory<Program>` contra Postgres real, sumados a los de `SmartDocDbContext`.
- **Fase 2 — patrón Job/Worker (ver ADR 0009):** entidad `ProcessingJob` (`Pending/Running/
  Done/Failed`), FK `ProcessingJob.DocumentId → Documents.Id` con `Cascade` (a diferencia de
  `Restrict` en `Document.UserId`, ver ADR 0006 — un job no tiene sentido sin su documento).
  `POST /api/documents` ahora crea también el `ProcessingJob` y devuelve `202 Accepted` (antes
  `201 Created`). `SmartDoc.Worker` corre `ProcessingJobPollingWorker`
  (`BackgroundService`, polling cada `Worker:PollingIntervalSeconds`, default 5s) que delega en
  `ProcessingJobProcessor` (`SmartDoc.Infrastructure`, testeable sin el loop). El
  "procesamiento" en esta fase es un placeholder (sin AI real todavía — eso es Fase 3):
  prueba el mecanismo asíncrono en sí, no contenido. Sin retry automático todavía (`RetryCount`
  existe en el schema pero nada lo consume aún). Verificado end-to-end corriendo el Worker
  como proceso real. 10 tests nuevos (unit + integration); tests de integración ahora corren
  secuenciales (`DisableTestParallelization`) por compartir la misma DB real sin aislamiento.
- **Fase 3 (en progreso) — object storage real (ver ADR 0010):** MinIO en
  `docker-compose.yml`, puerto `IFileStorage` en `SmartDoc.Application` (primer código real
  de esa capa) implementado con `MinioFileStorage`/`AWSSDK.S3` en Infrastructure, bucket
  creado de forma idempotente al arrancar la Api. `POST /api/documents` pasa de JSON a
  `multipart/form-data`, valida `ContentType == application/pdf` explícitamente, y sube el
  archivo real. `DELETE /api/documents/{id}` ahora también borra el objeto de MinIO (gap
  encontrado y corregido en esta misma ronda). Encontrado y resuelto: los endpoints con
  `IFormFile` requieren `.DisableAntiforgery()` desde .NET 8 o explotan en runtime. 51 tests
  totales, verificado además con la Api corriendo como proceso real.
- Embeddings: confirmado Ollama corriendo en la máquina física (no la VM) con
  `nomic-embed-text` (768 dimensiones) — ver `.env.example`. Sin auto-fallback entre
  proveedores (contradice el scope guard de `CLAUDE.md`); si la latencia de Ollama resulta un
  problema real, el swap a otro `IEmbeddingProvider` es solo implementación + config, sin
  tocar el resto del código.
- Pendiente al construir `DocumentChunks` (próximo paso): cada chunk guarda qué modelo lo
  generó (`EmbeddingModel` por-chunk, no global), y la dimensión del vector queda fija en el
  schema (`vector(768)` con `nomic-embed-text`) — cambiar de modelo a otra dimensión requiere
  migration + reembeder todo, no es un swap de config.
- **`ai-service-python` scaffold (ver ADR 0011):** FastAPI, routers `/parse` (pypdf, sin
  OCR — limitación conocida del MVP) y `/chunk` (chunking por página, no cruza saltos de
  página — necesario para citas "archivo — página N"; `tiktoken cl100k_base` como estimador
  de tokens aproximado, no exacto; defaults `chunk_size_tokens=500`/`overlap_tokens=75`).
  `ai-service` activado en `docker-compose.yml`. Verificado además con el contenedor Docker
  real corriendo (no solo el venv local).
- **`/embed` con Ollama (ver ADR 0012):** conectividad confirmada del contenedor hacia
  `192.168.56.1:11434` — `nomic-embed-text`, 768 dim, ~0.14-0.66s de respuesta (rápido; la
  lentitud original era por los modelos de 14B/32B, no aplica a un modelo de embeddings
  dedicado), soporta batch nativo. `EmbeddingProvider` (ABC) como equivalente Python de
  `IEmbeddingProvider`; la respuesta incluye `model` explícitamente para que .NET complete
  `DocumentChunk.EmbeddingModel` sin inferir nada. Fallas del proveedor devuelven `502`.
  15 tests totales (`pytest`), verificado también con el contenedor real.
- **`DocumentChunks` + wiring real del `ProcessingJobProcessor` (ver ADR 0013) — Fase 3
  cerrada.** `Embedding` como `float[]` en el dominio (no `Pgvector.Vector` — Domain libre de
  tipos de infraestructura, ADR 0005), conversión a `vector(768)` solo en la configuración
  EF, con `ValueComparer` explícito. Dimensión (768) y `EmbeddingModel` validados como
  invariantes de dominio en el constructor, no solo en el schema. `CREATE EXTENSION vector`
  agregado a la migration (`Pgvector.EntityFrameworkCore` solo mapea tipos, no habilita la
  extensión de Postgres — encontrado porque la primera corrida falló). `IAiServiceClient`
  (puerto en Application, `HttpClient` tipado en Infrastructure, JSON snake_case). El
  processor ahora hace `parse → chunk → embed → persistir chunks` real, reemplazando el
  placeholder de Fase 2. 12 tests nuevos, incluyendo un end-to-end contra el stack completo
  (Postgres + MinIO + ai-service + Ollama) con un PDF de fixture real. Verificado además con
  un smoke test manual: Api real + Worker real procesando un documento subido de verdad.
- **Fase 4 (en progreso) — `/generate` con Groq (ver ADR 0014):** Ollama local descartado
  para generación con datos reales (40.67s para 35 tokens, ~0.87 tok/s — inviable para un
  endpoint síncrono/user-facing, a diferencia de `/embed` que corre en background). Groq
  (`llama-3.3-70b-versatile` originalmente): 0.47s totales, ~330 tok/s, gratis. `LlmProvider`
  (ABC) mismo patrón que `EmbeddingProvider`. Citas NO delegadas al LLM — `/generate` solo
  recibe texto plano de contexto y devuelve la respuesta; .NET arma "Sources:" desde su propia
  metadata de retrieval (`FileName`/`PageNumber`), sin confiar en que el LLM cite bien.
  Encontrado y resuelto: la API de Groq (detrás de Cloudflare) rechaza requests sin
  `User-Agent` normal (error 1010, no es un error de auth). 4 tests nuevos, verificado también
  con el contenedor Docker real (env vars resueltas desde `.env` de la raíz vía
  `docker-compose.yml`). **Actualizado más tarde (durante Fase 5):** Groq deprecó
  `llama-3.3-70b-versatile` de su catálogo; reemplazado por `openai/gpt-oss-120b`
  (`GROQ_MODEL` en `.env`/`config.py`) tras comparar en vivo contra `openai/gpt-oss-20b`.
- **`Conversations`/`Messages` (ver ADR 0015).** Citas guardadas como parte de `Content`
  (prosa + "Sources:"), no en tabla separada — `PROJECT.md` no la pide. FKs siguiendo
  precedentes ya establecidos: `Conversation → User` `Restrict` (ADR 0006), `Message →
  Conversation` `Cascade` (ADR 0009). 12 tests nuevos.
- **Similarity search + `POST /api/search`/`POST /api/chat`/`GET /api/chat/{conversationId}`
  (ver ADR 0016) — Fase 4 cerrada.** `SimilaritySearchService` con SQL crudo (operador
  coseno `<=>` de pgvector vía `Database.SqlQuery<T>`), sin interfaz (SQL específico de
  Postgres, no hay implementación alternativa que abstraer). Sin índice de similaridad
  todavía — sequential scan alcanza a escala de PoC, se agrega si el volumen lo justifica.
  `Rag:MaxRelevantDistance = 0.75` como threshold de partida, explícitamente pendiente de
  ajuste empírico (`PROJECT.md` lo describe como "configurable"). Si nada pasa el
  threshold, .NET devuelve "insufficient context" sin llamar a `/generate` (ahorra
  tiempo/costo). 13 tests nuevos (96 tests totales del lado .NET en Fase 4). Verificado
  además con un smoke test manual completo: Api real + Worker real + `/api/chat` real
  citando correctamente el documento procesado.
- **Fase 5 (en progreso) — Auth (JWT) (ver ADR 0017).** Passwords hasheados en `Users.
  PasswordHash` con `PasswordHasher<User>` (PBKDF2, `Microsoft.Extensions.Identity.Core`),
  resincronizados en cada arranque de la Api por `SmartDocDbContextSeeder`. `POST
  /api/auth/login` devuelve un JWT (HMAC-SHA256, claims `sub`/`email`/`jti`,
  `Jwt:ExpirationMinutes` configurable) — mismo `401` si el email no existe o la password es
  incorrecta, para no permitir enumerar usuarios registrados. `DocumentEndpoints`/
  `ChatEndpoints`/`SearchEndpoints` ahora requieren `Authorization: Bearer <token>`
  (`RequireAuthorization()`) y derivan `UserId` del claim `sub` vía
  `ClaimsPrincipal.GetUserId()`, no del body/form — el chequeo "¿existe este UserId?" que
  existía antes se eliminó por quedar inalcanzable. `Documents` sigue siendo una base de
  conocimiento compartida (cualquier usuario ve/borra cualquier documento); `Conversations`
  pasó a ser personal (`GetConversationAsync` exige `UserId` del dueño, `404` sin distinguir
  "no existe" de "es de otro usuario"). Encontrado y resuelto en el camino: sin
  `MapInboundClaims = false` en `AddJwtBearer`, ASP.NET Core remapea `sub`/`email` a URIs
  largas de WS-Federation al construir el `ClaimsPrincipal`, rompiendo la lectura del claim
  por nombre corto. 17 tests nuevos (108 tests totales), `AuthTestHelper.AuthenticateAs`
  mintea tokens directo vía `JwtTokenGenerator` para no repetir el flujo de login en cada
  archivo de test. Verificado además con un smoke test manual de punta a punta con la Api
  real: sin token → `401`, login con password incorrecta → `401`, login correcto → token,
  endpoint protegido con ese token → `200`.
- **Retry granular de `ProcessingJob` (ver ADR 0018).** `Worker:MaxRetries` (default 3,
  configurable) = reintentos después del intento inicial, no el total — con el default, un
  job puede correr hasta 4 veces antes de quedar permanentemente `Failed`. Sin backoff
  exponencial ni scheduling nuevo: un job que falla vuelve a `Pending` y el próximo poll del
  `ProcessingJobPollingWorker` (mismo intervalo de siempre) lo retoma — `PROJECT.md` ya
  dejaba "retries avanzados" como evolución post-MVP detrás de Hangfire/Quartz.
  `ProcessingJob.RecordFailure` (nuevo, separado de `MarkAsFailed` que sigue reservado para
  el caso irrecuperable "Document no existe") decide él mismo, comparando `RetryCount` contra
  `maxRetries`, si vuelve a `Pending` o pasa a `Failed` — la entidad es dueña de la
  transición, no el `ProcessingJobProcessor`. `Document` se queda en `Processing` mientras
  hay reintentos en curso; recién pasa a `Failed` cuando el job se agota (sin agregar ningún
  estado nuevo a `DocumentStatus`). 7 tests nuevos (116 totales), incluyendo un test de
  integración que fuerza fallos reales contra MinIO (`StoragePath` nunca subido, sin mocks) y
  verifica la progresión completa `Pending(1) → Pending(2) → Failed(3)`. Verificado además
  con un smoke test manual: `SmartDoc.Worker` real, log en vivo mostrando "attempt 1/4" →
  "attempt 2/4" → "attempt 3/4" → fallo permanente recién en el cuarto intento
  (`RetryCount = 4`). Encontrado de paso: `SmartDoc.Worker` (Generic Host puro, no ASP.NET
  Core) usa `DOTNET_ENVIRONMENT`, no `ASPNETCORE_ENVIRONMENT`, para cargar
  `appsettings.Development.json` al correrlo suelto.
- **Índice HNSW sobre `DocumentChunks.Embedding` (ver ADR 0019).** `DocumentChunks` sigue en
  0 filas en este entorno — se documenta sin vueltas que esto es una decisión de arquitectura
  para portfolio, no una optimización motivada por un cuello de botella medido. HNSW en vez
  de `ivfflat` por una razón concreta de este proyecto: `ivfflat` clusteriza (k-means) a
  partir de los datos presentes al momento de `CREATE INDEX`, y esta tabla arranca vacía y
  crece de a un documento por vez — generaría clusters degenerados. HNSW construye su grafo
  de forma incremental, sin depender de datos representativos de entrada.
  `vector_cosine_ops` matchea el operador `<=>` que ya usa `SimilaritySearchService` (ADR
  0016); `m`/`ef_construction` en los defaults de pgvector, sin tunear (mismo criterio que
  `Rag:MaxRelevantDistance`: sin tráfico real no hay contra qué calibrar). Verificado
  cargando temporalmente 5000 filas sintéticas y confirmando con `EXPLAIN (ANALYZE, BUFFERS)`
  que el plan pasa de `Seq Scan` a `Index Scan using "IX_DocumentChunks_Embedding"` — hallazgo
  real en el camino: el primer intento, recién después del bulk insert y sin `ANALYZE`, el
  planner ignoró el índice por estadísticas desactualizadas (en producción, autovacuum lo
  resuelve solo). Datos sintéticos borrados al terminar, 116 tests sin cambios (el índice no
  altera resultados, solo el plan de ejecución).
- **Serilog real + manejo global de excepciones + logging de `Distance` crudo, en archivo y
  consola, en los tres procesos (ver ADR 0020).** Hallazgo en el camino (dos, en realidad):
  `Serilog.AspNetCore` estaba referenciado en `SmartDoc.Api.csproj` desde el inicio del
  proyecto pero nunca se llamó a `UseSerilog` — la Api corrió Fases 1-4 sobre el logger de
  consola por defecto; `SmartDoc.Worker` no tenía Serilog en absoluto. Segundo hallazgo, ya
  corrigiendo el primero: la decisión inicial de "solo sink de consola, `docker logs` ya
  captura stdout" resultó **incorrecta** — `Api`/`Worker` no corren containerizados
  (`docker-compose.yml` solo tiene `postgres`/`minio`/`ai-service`), corren sueltos vía
  `dotnet run`, así que sin sink de archivo el log de `Distance` (la razón misma de este
  trabajo) se perdía apenas se cerraba la terminal. Corregido: **consola en texto legible +
  archivo en JSON estructurado (`CompactJsonFormatter`/CLEF)**, mismo criterio en `Api`,
  `Worker` (`logs/api-*.log`, `logs/worker-*.log`, rolling diario) y también en `ai-service`
  (Python, `logging_config.py`/`.json`, formatter JSON propio sin dependencia nueva, aplicado
  vía `--log-config` de uvicorn — no a nivel de import, para no competir con la config propia
  de uvicorn — `docker-compose.yml` monta `./ai-service-python/logs:/app/logs` para que sea
  visible desde el host). El `Distance` crudo además se aísla en `logs/distance-.log` (Api)
  vía un sub-logger de Serilog filtrado por categoría (`RagDistanceLog.CategoryName`), sin
  dejar de aparecer también en el log general. `GlobalExceptionHandler` (`IExceptionHandler`,
  .NET 8+) reemplaza el 500 vacío/dev-exception-page por `ProblemDetails` consistente — mapeo
  deliberadamente angosto: `HttpRequestException` (ai-service/Groq/Ollama caído) → 503, todo
  lo demás → 500 genérico sin filtrar `exception.Message` en la respuesta. 3 tests .NET nuevos
  (2 unit sobre `GlobalExceptionHandler.MapException`, 1 integración que fuerza una
  `HttpRequestException` real a través del pipeline completo reemplazando `IAiServiceClient`
  por un doble vía `ConfigureServices` — el primer intento, apuntar `AiService:BaseUrl` a un
  puerto sin listener vía `ConfigureAppConfiguration`, no funcionó de forma confiable y se
  descartó). 119 tests .NET totales (65 unit + 54 integración) + 19 tests de `ai-service`,
  todos corridos contra el stack real completo (Docker Compose: Postgres, MinIO, ai-service
  reconstruido con el nuevo logging; Ollama real en la máquina física para embeddings, Groq
  real para generación) — todos en verde. Verificado además con los tres procesos corriendo
  de verdad (no solo tests): `dotnet run` de `Api`/`Worker` generando los tres archivos JSON
  esperados con contenido correcto, y `ai-service` en Docker con el volumen montado mostrando
  `logs/ai-service.log` en el host — encontrado y corregido en el camino un campo `asctime`
  que se filtraba como ruido (efecto colateral de que Python comparte el mismo `LogRecord`
  entre handlers, no un dato real).
- **Calibración empírica de `Rag:MaxRelevantDistance` (ver ADR 0022) — de `0.75` a `0.33`.**
  Corpus real de 6 PDFs variados (16-272 páginas) + 45 preguntas con ground truth (24 directas,
  12 parafraseadas, 9 negativas fuera de alcance del corpus), corridas contra `POST
  /api/search` (no `/api/chat` — da el `Distance` crudo sin aplicar threshold ni gastar en
  Groq). Hallazgo principal: con `0.75` **ninguna** de las 45 preguntas quedaba filtrada
  (máxima distancia observada: `0.52`) — el fallback de "insufficient context" era código
  muerto en la práctica. `0.33` da cero falsos positivos entre las negativas con 91.7% de
  recall en las positivas — precisión priorizada sobre recall a propósito (una cita falsa
  mina más confianza que un "no tengo información" de más). `scripts/calibrate-rag-
  threshold.ps1` automatiza el ciclo completo; `calibration/` (PDFs/preguntas/resultados
  crudos) queda gitignoreada, el ADR es el registro durable. El corpus se dejó cargado en la
  base de dev (real, no descartable) — `DocumentChunks` deja de estar en 0 filas.

Decisiones conscientes, no pendientes olvidados:
- **Endpoints de `Users`** (registro, cambio de password) — deliberadamente fuera de scope
  (ver ADR 0007): el único usuario del MVP sigue siendo el seed user gestionado por config,
  sin registro público ni flujo de reset de password.
- **Sin revocación de tokens.** El claim `jti` se emite pero no se persiste ni se chequea
  contra ninguna lista — sin logout real; aceptable para un solo seed user (ver ADR 0017).

Próximo paso: seguir con Fase 5 (Production polish). Candidato ya identificado: ajuste
empírico del threshold de similaridad (`Rag:MaxRelevantDistance`, ADR 0016), para lo cual ADR
0020 ya deja el logging de `Distance` crudo instrumentado y verificado — requiere generar
tráfico real (ver metodología acordada: 5-10 PDFs variados + ~30-50 preguntas de
calibración), no bloqueante.
