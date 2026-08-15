# ADR 0009 — Fase 2: patrón Job/Worker (async processing)

**Status:** Aceptado

## Contexto

`PROJECT.md` especifica el flujo `Upload → ... → Create processing job → Return 202
Accepted` y una tabla `ProcessingJobs` (`Id, DocumentId, Status, RetryCount, ErrorMessage,
CreatedAt, UpdatedAt`) con un Worker (`BackgroundService` + polling simple) que la procesa.
El upload real de archivo (`Store original file`) queda diferido a Fase 3 — ver discusión de
esa ronda: no hay object storage provisionado, y nadie necesita leer el archivo todavía
(ni siquiera el Worker de esta fase, que no hace parsing real).

## Decisiones

**`ProcessingJob` como entidad de dominio propia**, mismo patrón que `User`/`Document`
(ctor privado + público validante, métodos de transición explícitos: `MarkAsRunning`,
`MarkAsDone`, `MarkAsFailed`). Estados `Pending/Running/Done/Failed` (PascalCase, igual
convención que `DocumentStatus`), persistidos como string.

**FK `ProcessingJob.DocumentId → Documents.Id` con `Cascade`**, a diferencia de
`Document.UserId → Users.Id` que usa `Restrict` (ADR 0006). La diferencia es intencional:
un `User` tiene valor de auditoría independiente de sus `Documents` (por eso `Restrict`); un
`ProcessingJob` no tiene ningún sentido sin su `Document` — si se borra el documento
(`DELETE /api/documents/{id}`, ya existente desde Fase 1), sus jobs deben irse con él.

**`POST /api/documents` ahora crea el `Document` y el `ProcessingJob` (`Pending`) en la
misma transacción, y devuelve `202 Accepted`** (antes `201 Created`) con `Location` apuntando
a `GET /api/documents/{id}` para que el cliente haga polling del `Status`. Es un cambio de
contrato sobre el endpoint de Fase 1 — actualizado en los tests existentes.

**La lógica de "tomar y procesar un job" vive en `ProcessingJobProcessor`
(`SmartDoc.Infrastructure/Processing/`), separada del `BackgroundService`.** El
`BackgroundService` (`ProcessingJobPollingWorker`, en `SmartDoc.Worker`) es solo el loop de
polling (intervalo configurable vía `Worker:PollingIntervalSeconds`, default 5s) que crea un
scope de DI y llama al processor. Esta separación existe por una razón concreta: poder testear
la lógica de negocio del procesamiento sin levantar el polling loop completo — no es
abstracción por costumbre.

**El Worker de esta fase no hace trabajo de AI real** — no hay servicio Python todavía
(Fase 3). El "procesamiento" es un placeholder (`Task.Delay` + transición de estado) que
prueba el mecanismo asíncrono en sí (`Pending → Running → Done`, `Document: Uploaded →
Processing → Ready`). Se reemplaza por la llamada real a parse/chunk/embed en Fase 3.

**Sin retry automático todavía.** `RetryCount` existe en el schema (según `PROJECT.md`) y se
incrementa en `MarkAsFailed`, pero no hay ningún loop que reintente un job fallido — hoy no
hay ningún modo de falla real (el placeholder no puede fallar salvo una excepción
inesperada), así que un mecanismo de retry sería especulativo. Se construye en Fase 3, cuando
aparezcan fallos reales y con sentido para reintentar (servicio Python caído, error de
parsing).

**Tests de integración corren secuenciales, no en paralelo** (`AssemblyInfo.cs`,
`DisableTestParallelization = true`). Todos comparten la misma base Postgres real sin
aislamiento por test (sin transacciones, sin schema separado) — con los nuevos tests
de `ProcessingJobProcessor` (que buscan "el próximo job Pending" sin filtrar por un id
conocido), correr clases de test en paralelo podía hacer que un test le robara el job a otro.

## Consecuencias

- Migration `AddProcessingJobs` aplicada.
- Verificado end-to-end con el Worker corriendo como proceso real (no solo vía tests):
  insertado un job `Pending` a mano, el Worker lo tomó, `Document` pasó a `Ready` y `Job` a
  `Done`.
- El próximo punto de fricción esperado es Fase 3: ahí sí hace falta el archivo real (retoma
  la discusión de MinIO/`IFileStorage` diferida), y ahí es donde el retry automático empieza
  a tener sentido.
