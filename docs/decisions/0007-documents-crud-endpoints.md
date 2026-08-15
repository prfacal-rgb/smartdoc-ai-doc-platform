# ADR 0007 — Primeros endpoints CRUD (Documents) y herramientas OpenAPI

**Status:** Aceptado

## Contexto

Con `SmartDocDbContext`, `User` y `Document` ya persistidos (ADR previo + migrations), tocaba
exponer los primeros endpoints reales — momento que ADR 0003 dejaba explícitamente pendiente
para agregar Swagger/OpenAPI. Aparecieron tres decisiones no triviales: dónde vive la lógica
de cada endpoint (¿Application con una abstracción de repositorio, o acceso directo a EF Core
desde `SmartDoc.Api`?), cómo conseguir un `UserId` válido para poder probar `Documents` sin
tener todavía endpoints de `Users` ni auth, y qué stack de OpenAPI/Swagger usar.

## Decisiones

**Solo `Documents` en esta tanda, sin endpoints de `Users`.** No hay registro público (scope
guard de `CLAUDE.md`) ni JWT implementado todavía, así que no hay caso de uso real para
CRUD de `Users` — se agrega junto con auth.

**Los handlers de `SmartDoc.Api/Features/Documents/` inyectan `SmartDocDbContext`
directamente, sin repositorio ni capa intermedia en `SmartDoc.Application`.** Es la lectura
más directa de "Vertical Slice Architecture" (ADR 0003: "un endpoint = un caso de uso") y de
`CLAUDE.md`: "Repositorios/servicios detrás de interfaces cuando haya una razón concreta de
testabilidad o swap de implementación (no abstraer por costumbre)". No hay planes de cambiar
de proveedor de persistencia en este proyecto, así que una interfaz de repositorio hoy sería
ceremonia sin beneficio concreto. `SmartDoc.Application` queda sin código todavía — se usa
el día que aparezca lógica de negocio compleja que valga la pena aislar del transporte HTTP.

**DTOs (`CreateDocumentRequest`/`DocumentResponse`) y validación (FluentValidation) viven en
el mismo namespace del feature, en `SmartDoc.Api`.** Las entidades de EF Core nunca se
serializan directamente en la respuesta (`CLAUDE.md`).

**Fase 1 = solo metadata, sin archivo real.** `POST /api/documents` recibe JSON
(`FileName`/`ContentType`/`StoragePath` como valores ya conocidos), no `multipart/form-data`.
No hay object storage provisionado todavía (`docker-compose.yml` no tiene MinIO); conectar
upload real de bytes queda para cuando se implemente el pipeline async (Fase 2/3).

**Seed user mínimo al arrancar la Api (`SmartDocDbContextSeeder`).** Sin él, `POST
/api/documents` sería imposible de probar manualmente: la FK `Document.UserId → Users.Id`
(ADR 0006) rechaza cualquier `UserId` que no exista, y no hay endpoint de `Users` para crear
uno. El seeder es un `INSERT` idempotente basado en `Jwt:SeedUserEmail` (config ya prevista
en `CLAUDE.md`) — **no** es autenticación real; solo garantiza una fila de `User` válida.

**OpenAPI nativo de .NET (`Microsoft.AspNetCore.OpenApi`) + Scalar como UI, no Swashbuckle.**
Se probó `Swashbuckle.AspNetCore` primero (paquete más conocido), pero genera un conflicto de
versión real: `Swashbuckle.AspNetCore.SwaggerGen 7.2.0` está compilado contra
`Microsoft.OpenApi` v1.x, mientras que `Microsoft.AspNetCore.OpenApi` (el soporte nativo de
.NET 10) requiere v2.x — ambos paquetes comparten el mismo ensamblado `Microsoft.OpenApi` y
no pueden convivir con una sola versión cargada (falla en runtime con `TypeLoadException` en
`SwaggerGenerator.GetSwagger`, detectado por los integration tests). **Scalar.AspNetCore**
solo renderiza el documento OpenAPI que ya genera el soporte nativo (`AddOpenApi()` +
`MapOpenApi()`), sin dependencia propia de `Microsoft.OpenApi`, evitando el conflicto. UI
disponible en `/scalar/v1` (solo en `Development`).

## Consecuencias

- `SmartDoc.Application` sigue vacío — es una decisión consciente, no un olvido.
- Si en el futuro aparece una razón concreta (por ejemplo, lógica de negocio compleja
  compartida entre varios endpoints, o necesidad real de swap de persistencia), se introduce
  la abstracción en ese momento, no antes.
- `POST /api/documents` valida que el `UserId` exista (`404` si no) antes de intentar el
  insert, en vez de depender de que la FK tire una excepción de Postgres sin manejar.
- 8 integration tests end-to-end (`WebApplicationFactory<Program>`) cubren los 4 endpoints,
  incluyendo validación y los casos 404.
- `Program.cs` expone `public partial class Program` al final para que
  `SmartDoc.IntegrationTests` pueda usar `WebApplicationFactory<Program>`.
