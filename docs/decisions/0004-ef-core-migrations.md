# ADR 0004 — Estrategia de migrations con EF Core

**Status:** Aceptado

## Contexto

Con PostgreSQL + pgvector ya definido como base de datos, hacía falta decidir cómo se
gestionan los cambios de schema a lo largo del proyecto.

## Decisión

**Code-First con migrations automáticas** de EF Core (`dotnet ef migrations add`,
`dotnet ef database update`), ejecutadas desde `SmartDoc.Infrastructure` (donde vive el
`DbContext`). El paquete `Pgvector.EntityFrameworkCore` provee el mapeo tipado del tipo
`vector` de Postgres para la columna `DocumentChunks.Embedding`.

## Consecuencias

- El modelo de dominio (entidades en `SmartDoc.Domain`) es la fuente de verdad del schema;
  no se escribe SQL de DDL a mano salvo casos puntuales que EF Core no cubra bien (por
  ejemplo, la creación del índice de similaridad de pgvector, que probablemente requiera una
  migration con SQL crudo vía `migrationBuilder.Sql(...)`).
- Cada migration queda versionada en el repo (`src/SmartDoc.Infrastructure/Migrations/`),
  documentando la evolución del schema junto con el código.
- Pendiente de definir en una fase posterior: si `dotnet ef database update` se corre
  manualmente en dev o se automatiza al levantar el Worker/Api (aplicar migrations al
  arrancar). No se ha tomado esa decisión todavía.
