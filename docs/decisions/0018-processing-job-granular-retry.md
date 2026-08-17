# ADR 0018 — Retry granular de `ProcessingJob`

**Status:** Aceptado

## Contexto

Pendiente desde Fase 2 (ADR 0009): `ProcessingJob` ya tenía el campo `RetryCount` en el
schema, pero nada lo usaba — cualquier fallo durante `parse → chunk → embed` (ADR 0013)
marcaba el job y el `Document` como `Failed` de forma permanente en el primer intento, sin
segunda oportunidad. Un timeout puntual del ai-service o un hipo de red bastaban para que un
documento por lo demás válido quedara `Failed` para siempre, sin ninguna forma de recuperarse
salvo re-subirlo.

## Decisiones

**Retry acotado por conteo, sin backoff exponencial ni scheduling.** `PROJECT.md` ya
anticipaba esto: la tabla `ProcessingJobs` con polling simple es "suficiente para el volumen
de un PoC", dejando "retries avanzados o scheduling" explícitamente como evolución post-MVP
detrás de Hangfire/Quartz. Un job que falla vuelve a `Pending` y el próximo poll del
`ProcessingJobPollingWorker` (cada `Worker:PollingIntervalSeconds`, default 5s) lo vuelve a
levantar — el intervalo de polling ya existente hace de intervalo de retry, sin agregar
ninguna infraestructura nueva.

**`Worker:MaxRetries` (default 3) = reintentos después del intento inicial, no el total de
intentos.** Con el default, un job puede ejecutarse hasta 4 veces (1 inicial + 3 reintentos)
antes de quedar permanentemente `Failed`. Elegido así (en vez de "MaxRetries = intentos
totales") porque es la convención más común en librerías de retry (Polly, por ejemplo) y la
lectura más natural del nombre del campo de config — documentado explícitamente en el
docstring de `RecordFailure` para no dejarlo ambiguo.

**Dos métodos de dominio distintos en `ProcessingJob`, no uno solo con una rama de más.**
`MarkAsFailed` (existente, sin tocar) queda reservado para fallos estructuralmente
irrecuperables — hoy solo el caso guardado de "el `Document` referenciado no existe", que en
la práctica es inalcanzable dado el FK `Cascade` (ADR 0009) pero se mantiene como salvaguarda.
`RecordFailure` (nuevo) es el que se usa para cualquier excepción real del pipeline de
procesamiento (red, ai-service caído, parseo, etc.) — decide él mismo, comparando
`RetryCount` contra `maxRetries`, si el job vuelve a `Pending` o pasa a `Failed`. Separar los
dos evita una única función con una bandera booleana tipo `isRetryable` que el llamador tendría
que decidir "desde afuera" — la entidad es dueña de la transición de estado, no el processor.

**El `Document` permanece en `Processing` mientras el job reintenta, no pasa a `Failed` hasta
que el job se agota.** No se agregó ningún estado nuevo a `DocumentStatus` (evaluado y
descartado un hipotético `Retrying` — el estado `Processing` ya comunica correctamente "seguimos
trabajando en esto" sin necesidad de distinguir el primer intento de un reintento desde la
perspectiva del usuario). `document.MarkAsFailed()` solo se llama cuando
`job.Status == ProcessingJobStatus.Failed` después de `RecordFailure`.

**Sin cambios en la query de selección de `ProcessingJobProcessor.ProcessNextAsync`.** Un job
reintentable vuelve a `Pending`, así que reingresa naturalmente al mismo `WHERE Status =
Pending ORDER BY CreatedAt` que ya existía — queda en la cola FIFO junto con jobs nuevos,
ordenado por su `CreatedAt` original (no se resetea), por lo que un job viejo con reintentos
pendientes no le "roba" prioridad a uno nuevo más de lo que ya lo hacía antes de este cambio.

## Consecuencias

- `Worker:MaxRetries` agregado a `appsettings.Development.json` de `SmartDoc.Api` y
  `SmartDoc.Worker` (ambos con default explícito `3`, mismo patrón que `Rag:MaxRelevantDistance`
  — GetValue ya trae un default de código, pero se deja explícito en config para que sea
  visible sin leer el código).
- `ProcessingJobProcessor` ahora recibe `IConfiguration` en el constructor (antes no lo
  necesitaba) — único cambio de firma que se propaga a los tests que lo instancian
  directamente.
- 6 tests nuevos de `ProcessingJob.RecordFailure` (retry por debajo del límite, fallo
  permanente al superarlo, `maxRetries: 0` como caso borde, validaciones de argumentos) + 1
  test de integración nuevo que fuerza fallos reales contra MinIO (un `StoragePath` que nunca
  se subió, sin ningún mock) y verifica la progresión completa
  `Pending(1) → Pending(2) → Failed(3)` con el `Document` recién marcado `Failed` en el último
  paso. 116 tests totales (65 unit + 51 integration).
- Verificado además con un smoke test manual de punta a punta: `Document`/`ProcessingJob`
  insertados directamente con un `StoragePath` inválido, `SmartDoc.Worker` corriendo como
  proceso real contra Postgres/MinIO reales. Log observado en vivo: `"attempt 1/4, will
  retry"` → `"attempt 2/4"` → `"attempt 3/4"` → job y `Document` permanentemente `Failed`
  recién en el cuarto intento, con `RetryCount = 4`. El fallo real encontrado en el camino
  (`AmazonS3Exception` por mismatch de firma con un `StoragePath` que arrancaba con `/`, no el
  "key not found" esperado) terminó siendo una buena confirmación adicional: el mecanismo
  reintenta ante *cualquier* excepción no fatal del pipeline, no solo la que se había
  anticipado al diseñar el test.
- Hallazgo de entorno sin relación con el retry en sí, corregido de paso: `SmartDoc.Worker` es
  un Generic Host puro (`Host.CreateApplicationBuilder`), no ASP.NET Core — respeta
  `DOTNET_ENVIRONMENT`, no `ASPNETCORE_ENVIRONMENT`, para cargar `appsettings.Development.json`
  al correrlo como proceso suelto.
