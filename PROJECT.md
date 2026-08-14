# SmartDoc — AI-Powered Document Intelligence Platform

> Proyecto de portfolio (PoC). Objetivo: demostrar arquitectura backend moderna con AI como
> componente de producción, no como demo de "sé usar un LLM".

**Tagline:** A production-oriented document intelligence platform built with .NET and Python,
combining asynchronous backend processing, semantic search and RAG-based question answering
with source citations.

---

## 1. Concepto

Plataforma donde un usuario:

1. Sube documentos (PDF, inicialmente).
2. El sistema los procesa de forma asíncrona (no bloqueante).
3. Extrae texto y metadata.
4. Genera embeddings y los indexa.
5. Permite consultar los documentos en lenguaje natural.
6. Responde con RAG (Retrieval-Augmented Generation) + citas a los documentos originales
   (archivo + página).

**Ejemplo de uso:** subís 20 PDFs de documentación técnica y preguntás "¿Cómo se maneja la
autenticación y qué servicios intervienen?". El sistema busca los fragmentos relevantes, arma
el contexto y responde citando de qué documento/página sale cada afirmación.

Esto demuestra: arquitectura backend, diseño de APIs, persistencia, procesamiento asíncrono,
búsqueda semántica, AI orchestration, seguridad, testing y observabilidad — no solo "consumir
un LLM".

---

## 2. Arquitectura

Modular monolith en .NET + servicio de AI en Python. Se evita arrancar con microservicios:
la complejidad operativa no se justifica en esta etapa.

```
                    ┌─────────────────────┐
                    │   Client (Swagger)  │
                    └──────────┬──────────┘
                               │
                    ┌──────────▼──────────┐
                    │      .NET API        │
                    │ Documents / Search /  │
                    │ Chat / Auth / Jobs    │
                    └──────┬───────┬───────┘
                           │       │
                ┌──────────▼─┐   ┌▼──────────────┐
                │ PostgreSQL │   │ Object Storage │
                │ + pgvector │   │ (PDFs/files)   │
                └──────┬─────┘   └────────────────┘
                       │
                ┌──────▼──────────┐
                │ Background       │
                │ Worker (.NET)    │
                └──────┬───────────┘
                       │  HTTP (interno)
                ┌──────▼──────────┐
                │ Python AI        │
                │ Service (FastAPI)│
                │ Parsing/Chunking/│
                │ Embeddings/RAG   │
                └──────┬───────────┘
                       │
                ┌──────▼──────────┐
                │ LLM / Embedding  │
                │ Provider(s)      │
                │ (abstraído)      │
                └──────────────────┘
```

### ¿Por qué esta arquitectura?

- **.NET** → APIs, dominio, seguridad, persistencia, orquestación, jobs.
- **Python** → ecosistema AI/ML, procesamiento de documentos, embeddings, RAG.

No se reemplaza .NET por Python: se elige la herramienta correcta para cada parte del sistema.

### Contrato .NET ↔ Python (antes implícito, ahora explícito)

El flujo de una pregunta es:

1. `.NET` recibe la pregunta del usuario (`POST /api/chat`).
2. `.NET` llama a `Python /embed` para vectorizar la pregunta.
3. `.NET` ejecuta la similarity search directamente contra `pgvector` (no la hace Python).
4. `.NET` arma el contexto (top-K chunks + metadata de citación).
5. `.NET` llama a `Python /generate` con la pregunta + contexto.
6. `Python` devuelve la respuesta generada; `.NET` la persiste y la devuelve al cliente con
   las citas.

Esto mantiene a Python **stateless** y sin acceso directo a la base — simplifica seguridad y
testing.

---

## 3. Proveedores de LLM y Embeddings (abstraídos)

**Decisión de diseño clave:** ni el LLM ni el modelo de embeddings quedan hardcodeados.
Se define una interfaz/puerto para cada uno, con implementaciones intercambiables:

- `ILlmProvider` (o equivalente en Python): Anthropic, OpenAI, Ollama (local).
- `IEmbeddingProvider`: OpenAI, Ollama/sentence-transformers (local), Anthropic no aplica
  (no ofrece embeddings).

Configuración vía variables de entorno / `appsettings`, seleccionable sin cambiar código de
negocio (Strategy pattern en el punto de integración). Punto de partida sugerido, por costo:
**Anthropic para chat + Ollama local para embeddings**, con posibilidad de swap a OpenAI en
cualquiera de los dos roles.

> Esto es, en sí mismo, un punto fuerte para el README: demuestra diseño para portabilidad de
> proveedor, no acoplamiento a un vendor.

---

## 4. Componentes

### A. .NET API

- ASP.NET Core (**.NET 9**)
- Clean Architecture / Vertical Slice Architecture
- Entity Framework Core
- PostgreSQL
- JWT authentication (ver sección 4.C — alcance simplificado para MVP)
- OpenAPI / Swagger
- FluentValidation
- Serilog

**Endpoints iniciales:**

```
POST   /api/documents
GET    /api/documents
GET    /api/documents/{id}
DELETE /api/documents/{id}
POST   /api/documents/{id}/process
POST   /api/search
POST   /api/chat
GET    /api/chat/{conversationId}
```

API deliberadamente pequeño y claro — no se agregan endpoints sin necesidad concreta.

### B. Background Worker (.NET)

Mecanismo por defecto: `BackgroundService` nativo de .NET + tabla `ProcessingJobs` con
polling simple (status: `PENDING/RUNNING/DONE/FAILED`, `RetryCount`). Es suficiente para el
volumen de un PoC y evita agregar infraestructura (Hangfire/Quartz quedan como evolución
post-MVP si se necesita dashboard, retries avanzados o scheduling).

### C. Auth (alcance MVP)

JWT con un único usuario semilla (seed user) para simplificar el PoC — **no** se implementa
registro público en el MVP. Esto se documenta explícitamente como simplificación consciente,
no como omisión.

```
POST /api/auth/login   → devuelve JWT (usuario semilla, credenciales por config)
```

### D. Python AI Service

FastAPI, stateless, expone solo las operaciones AI necesarias:

```
POST /parse      → extracción de texto desde PDF
POST /chunk       → división en chunks
POST /embed        → vectorización (texto o pregunta)
POST /generate    → respuesta LLM dado contexto
```

---

## 5. Procesamiento de documentos

```
Upload → Validate → Store original file → Create Document record
       → Create processing job → Return 202 Accepted
```

El API no espera a que termine el procesamiento (importante: demuestra pensamiento backend
real, no solo un CRUD).

```
Background Worker → Extract text → Split into chunks → Generate embeddings
                   → Store chunks + vectors → Document = READY
```

**Tipos de archivo soportados en MVP:** PDF únicamente (se documenta como limitación
conocida, no como scope creep evitado).

---

## 6. Base de datos

PostgreSQL + pgvector (vector search y datos transaccionales en el mismo sistema — se evita
sumar Pinecone/Weaviate/Elasticsearch/Redis/Kafka sin una razón concreta).

```
Users
-----
Id, Email, CreatedAt

Documents
---------
Id, UserId, FileName, ContentType, StoragePath, Status, CreatedAt

DocumentChunks
--------------
Id, DocumentId, ChunkIndex, Text, PageNumber, Embedding (vector, dim según modelo elegido)

Conversations
-------------
Id, UserId, CreatedAt

Messages
--------
Id, ConversationId, Role, Content, CreatedAt

ProcessingJobs          ← agregado (no estaba en el borrador original)
--------------
Id, DocumentId, Status, RetryCount, ErrorMessage, CreatedAt, UpdatedAt
```

> Nota de README sugerida: *"For the MVP I chose PostgreSQL + pgvector to reduce operational
> complexity and keep transactional data and vector search in one system."*

**Dimensión del vector**: depende del modelo de embeddings elegido (ver sección 3) — se
define en config, no hardcodeado en el schema si es posible (o se documenta el valor exacto
usado si EF Core lo requiere fijo).

---

## 7. RAG

```
Question → Embedding(question) → Vector similarity search (pgvector)
        → Top-K relevant chunks → Build context → LLM → Answer + citations
```

**Parámetros de partida (ajustables, deben quedar documentados en el README):**

- Chunk size: ~500 tokens
- Overlap: ~50-100 tokens
- Top-K: 5 (configurable)
- Similarity threshold: configurable, con fallback a "insufficient context"

**Requisito obligatorio:** la respuesta debe citar fuentes.

```
La autenticación utiliza JWT y el servicio Auth genera los tokens después de validar
las credenciales.

Sources:
architecture.pdf — page 14
authentication.md — section 3.2
```

Esto convierte un chatbot genérico en una herramienta de document intelligence defendible
profesionalmente.

---

## 8. MVP — scope exacto

### ✅ Incluye

**Documents:** upload PDF, validación tamaño/tipo, persistencia, metadata, status
(`UPLOADED/PROCESSING/READY/FAILED`).

**Processing:** background job, extracción de texto, chunking, embeddings, persistencia de
chunks + vectors.

**Search:** semantic search, top-K configurable, documentos/páginas encontradas.

**Q&A:** pregunta sobre documentos, RAG, respuesta LLM, citations, comportamiento
"I don't know / insufficient context".

**Backend:** REST API, Swagger, auth básica (seed user), unit tests, integration tests,
structured logging, Docker Compose.

**Documentation:** diagrama de arquitectura, documentación de API, setup instructions,
architecture decisions (ADRs), explicación RAG, limitaciones conocidas.

### ❌ Fuera del MVP

Microservices, Kubernetes, event sourcing, Kafka, multi-agent system, fine-tuning, mobile
app, frontend complejo (arranca solo Swagger/API — frontend se evalúa después), billing,
multi-tenancy enterprise, múltiples tipos de documento, múltiples LLM providers simultáneos
(sí queda la *abstracción*, pero no la integración de 5 a la vez).

---

## 9. Fases

| Fase | Contenido |
|---|---|
| 1 — Backend foundation | .NET + PostgreSQL. API → EF Core → PostgreSQL. Documentos, usuarios, estados. |
| 2 — Async processing | API → Job → Worker → Document processing. |
| 3 — AI pipeline | PDF → Text → Chunks → Embeddings → pgvector. |
| 4 — RAG | Question → Retrieval → Context → LLM → Cited answer. |
| 5 — Production polish | Tests, logs, Docker, error handling, configuración, seguridad, docs. |
| 6 — Frontend (post-MVP) | UI básica (React, a evaluar) sobre el backend ya funcional. |

No se agregan features nuevas en Fase 5: se pule lo que ya existe.

---

## 10. README del portfolio — estructura sugerida

- **Architecture**: .NET 9, Python/FastAPI, PostgreSQL, pgvector, Docker, LLM API (abstraído).
- **Why .NET + Python?**
- **Why PostgreSQL + pgvector?**
- **Why async processing?**
- **Why provider abstraction for LLM/embeddings?** ← agregado, dado el enfoque de costos
- **RAG strategy**: chunk size, overlap, embedding model, top-K, similarity threshold,
  context construction, citations.
- **Trade-offs**, por ejemplo: *"I intentionally chose a modular monolith instead of
  microservices because the expected scale doesn't justify the operational complexity at
  this stage."*
- **Known limitations**: solo PDF, auth de un solo usuario, sin multi-tenancy.

---

## 11. Evolución post-MVP

**V2 — Evaluation:** dataset de preguntas/respuestas + métricas (retrieval accuracy,
citation accuracy, hallucination rate, latencia, token usage). Pasa el proyecto de
"I built a RAG application" a "I built and evaluated a RAG system".

**V3 — Resilience:** retries, timeouts, circuit breaker, rate limiting, tracing, metrics.

**V4 — Frontend:** React (u otra librería, a evaluar) sobre el backend ya estabilizado.

---

## Cambios respecto al borrador original

- Se fijó `.NET 9` de forma consistente (antes solo aparecía en un punto).
- Se explicitó el contrato de comunicación .NET ↔ Python (antes implícito).
- Se definió el alcance real de "authentication básica" (seed user, sin registro público).
- Se agregó tabla `ProcessingJobs` al modelo de datos (necesaria para el patrón job/worker
  descripto, pero ausente en el modelo original).
- Se fijaron valores de partida para chunking/top-K (antes se mencionaban como "a explicar"
  sin valores concretos).
- Se agregó la abstracción de proveedor LLM/Embeddings como decisión de diseño explícita
  (por requerimiento de flexibilidad de costos).
- Se aclaró mecanismo de background jobs por defecto (BackgroundService + polling, no
  Hangfire, para mantener el MVP simple).
- Se movió el frontend a fase explícita post-MVP en vez de dejarlo "a evaluar" sin ubicación
  en el plan.
