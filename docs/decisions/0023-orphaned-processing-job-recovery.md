# ADR 0023 — Recuperación de `ProcessingJob` huérfanos tras un cierre abrupto del `Worker`

**Status:** Aceptado

## Contexto

Encontrado en el camino de ADR 0021: cuando `SmartDoc.Worker` se cae de forma abrupta (crash,
`kill`, corte de luz) mientras tiene un job en curso, ese `ProcessingJob` queda en estado
`Running` — `MarkAsRunning`/`document.MarkAsProcessing()` ya se habían guardado antes del
`try` en `ProcessingJobProcessor.ProcessNextAsync`, pero el proceso murió antes de llegar a
`MarkAsDone`/`RecordFailure`. `ProcessingJobPollingWorker` solo recoge jobs `Status ==
Pending`, así que ese job queda huérfano para siempre: sin reintentar, sin loguear nada, sin
ninguna forma de que el sistema se dé cuenta por su cuenta. El caso real: el documento de 399
páginas de ADR 0022 quedó así tras el crash que motivó el fix del timeout — se resolvió esa vez
con un `UPDATE` manual por SQL directo, dejado explícitamente como pendiente hasta ahora.

## Decisiones

**Reencolar al arrancar el `Worker`, no un chequeo periódico de staleness.** Este proyecto
corre una sola instancia de `Worker` (sin Kubernetes/orquestación — scope guard de
`CLAUDE.md`), así que cualquier job en `Running` cuando una instancia nueva arranca solo puede
pertenecer a una instancia anterior que ya no existe — no hace falta inferir "abandono" con un
timeout de staleness (heartbeat, `UpdatedAt` + umbral), que además agregaría un tunable más
para calibrar sin necesidad real. Un chequeo periódico serviría para el caso "el mismo proceso
sigue vivo pero un job se cuelga sin tirar excepción" — ese caso ya quedó cubierto
indirectamente por ADR 0021 (todo lo que podía colgarse silenciosamente, los timeouts de HTTP,
ahora eventualmente tira una excepción capturable).

**`RecoverOrphanedJobsAsync` (nuevo, en `ProcessingJobProcessor`) reutiliza `RecordFailure`, no
resetea `RetryCount` a cero.** Decisión discutida explícitamente, no obvia: resetear el
contador sería más "justo" para un job que quedó huérfano por un motivo ajeno a su propio
contenido (ej. un `Ctrl+C` durante un redeploy, no necesariamente un crash real) — pero
resetear también anula la única protección contra un "poison pill": un documento que hace
caer al `Worker` cada vez que se lo procesa reintentaría para siempre entre reinicios
(crashea → reinicia → contador en 0 → se vuelve a agarrar → crashea de nuevo), sin llegar
nunca a `Failed`. Elegido no resetear, por consistencia además: `RecordFailure` ya trata por
igual una falla "culpa del documento" (PDF corrupto) y una "culpa del entorno" (Ollama caído) —
tratar distinto a la que viene de un `Worker` reiniciado sería el único caso especial de todo
el sistema de retry. Costo aceptado: un job que ya había fallado 1-2 veces por algo transitorio
y encima queda huérfano por un reinicio pierde uno de sus pocos intentos restantes por un
motivo ajeno al documento — mismo patrón de "recuperación manual aceptable" (re-subir el
documento) que ya existe en otras partes del proyecto (ver ADR 0007, sin revocación de tokens
en ADR 0017).

**Corre una sola vez al arrancar, antes de empezar a pollear — no dentro del loop de
polling.** `ProcessingJobPollingWorker.ExecuteAsync` abre un scope dedicado, llama
`RecoverOrphanedJobsAsync` una vez, y recién después entra al `while` de siempre. Correrlo
dentro del loop en cada iteración sería una query extra (`WHERE Status = Running`) cada
`Worker:PollingIntervalSeconds` sin ningún beneficio — en este diseño (instancia única,
recuperación en el arranque) no hay forma de que aparezcan jobs `Running` nuevos entre
iteraciones salvo por otro crash, que de nuevo dispara este mismo método en el próximo arranque.

**Mismo hallazgo, mismo archivo — el loop externo de `ProcessingJobPollingWorker` tenía el
mismo bug que ADR 0021 arregló adentro de `ProcessingJobProcessor`.** `catch (Exception ex)
when (ex is not OperationCanceledException)` en el `while` principal excluía
`TaskCanceledException` del mismo modo. Hoy es menos peligroso porque casi todo ya se atrapa
adentro de `ProcessNextAsync`, pero una excepción de Postgres antes de llegar a ese `try` (ej.
un timeout de comando ligado al `CancellationToken`) todavía podía escaparse acá y tumbar el
proceso. Corregido con el mismo criterio: `catch (Exception ex) when
(!stoppingToken.IsCancellationRequested)`.

## Consecuencias

- 3 tests de integración nuevos (`RecoverOrphanedJobsAsync_*`): un job huérfano con reintentos
  disponibles vuelve a `Pending` (`RetryCount` incrementado, `Document` sigue `Processing`); un
  job huérfano sin reintentos disponibles (`maxRetries: 0`) pasa a `Failed` junto con su
  `Document`; sin jobs `Running`, devuelve `0`. 122 tests .NET totales (65 unit + 57
  integración).
- Sin cambios de schema — reutiliza `ProcessingJobStatus.Running` y `RecordFailure`
  existentes, ningún estado ni columna nueva.
- No verificado con un crash real simulado del `Worker` en esta sesión (sí lo estaba, sin
  querer, el escenario real de ADR 0021/0022 que motivó este ADR) — la cobertura de integración
  contra Postgres real se consideró suficiente para el mecanismo en sí.
