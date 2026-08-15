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
   estados. Sin AI todavía. *(en progreso — ver "Estado actual")*
2. **Async processing** — Job/Worker pattern. Upload devuelve `202 Accepted` sin esperar
   procesamiento.
3. **AI pipeline** — Servicio Python (parse/chunk/embed) + integración con .NET Worker.
4. **RAG** — Retrieval + construcción de contexto + generación + citas.
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

# Proveedores AI (abstraídos — completar según implementación elegida)
LLM_PROVIDER=anthropic|openai|ollama
EMBEDDING_PROVIDER=ollama|openai
ANTHROPIC_API_KEY=
OPENAI_API_KEY=
OLLAMA_BASE_URL=

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

**Fase actual: 1 — Backend foundation (en progreso).**

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

Pendiente antes de cerrar la Fase 1:
- Endpoints CRUD de `Documents`/`Users` (con FluentValidation) — todavía no existe ningún
  endpoint más allá del scaffold inicial.

Próximo paso: primeros endpoints CRUD de `Documents`/`Users`.
