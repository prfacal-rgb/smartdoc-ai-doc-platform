# ADR 0021 — Robustez del pipeline de ingesta: tres bugs encontrados calibrando el corpus de RAG

**Status:** Aceptado

## Contexto

Armando el corpus de calibración de ADR 0022 (6 PDFs variados, 16-272 páginas, para ajustar
`Rag:MaxRelevantDistance` con tráfico real), tres de esos documentos fallaron su procesamiento
por razones que no eran del corpus en sí — eran gaps reales del pipeline `parse → chunk →
embed` (Fase 3, ADR 0013) que nunca se habían topado con documentos "del mundo real":
`DocumentChunks` seguía en 0 filas hasta esta sesión (ver "Estado actual" en `CLAUDE.md`), así
que el código solo había corrido contra los fixtures pequeños y limpios de los tests de
integración. Se documentan juntos porque surgieron de la misma sesión y comparten la misma
causa raíz de fondo.

## Decisiones

**PDFs con contraseña de usuario vacía ya no se rechazan de entrada (`ai-service`,
`pdf_parser.py`).** `MemoriasDeUnIngeniero.pdf` fallaba `/parse` con 400 ("PDF is encrypted")
de forma inmediata y consistente. Es un caso común: muchos PDFs "protegidos" restringen
impresión/copia con una contraseña de *owner*, pero tienen contraseña de *usuario* vacía —
cualquier lector normal los abre sin pedir nada. `reader.is_encrypted` no distingue esos dos
casos. Corregido: si está encriptado, se intenta `reader.decrypt("")` antes de rechazar; solo
se lanza `InvalidPdfError` si esa contraseña vacía también falla (contraseña real requerida).
Confirmado con el archivo real: `decrypt("")` devuelve éxito, 94 páginas legibles.

**Chunks con texto solo-whitespace ya no llegan al dominio (`ai-service`, `chunker.py`).**
`Get Hired with AI.pdf` fallaba permanentemente con `ArgumentException: Text cannot be empty`
en el constructor de `DocumentChunk` — una página sin texto extraíble (portada/imagen) producía
tokens no-vacíos que decodificaban a solo espacios/control, así que el guard existente
(`if not tokens: continue`) no la filtraba. El invariante del dominio (`string.
IsNullOrWhiteSpace`) rechazaba correctamente ese chunk, pero **todo el documento** moría por
una sola página así. Corregido en dos puntos de `chunk_pages`: se salta una página cuyo
`page.text.strip()` sea vacío antes de tokenizar, y se salta también una ventana cuyo texto
decodificado sea solo-whitespace (puede pasar en el borde final de una página aunque el resto
tenga contenido real). *Nota de implementación:* el primer intento de este segundo filtro tenía
un bug de `continue` sin avanzar `start += step` — bucle infinito potencial, corregido antes de
llegar a producción.

**Un timeout de HTTP ya no tumba el proceso `Worker` completo
(`ProcessingJobProcessor.cs`).** El documento más grande del corpus (399 páginas, después
excluido — ver ADR 0022) hizo que el `/embed` batcheado superara el timeout de `HttpClient`,
lanzando `TaskCanceledException`. El catch existente,
`catch (Exception ex) when (ex is not OperationCanceledException)`, excluía ese tipo de
excepción del retry granular (ADR 0018) porque `TaskCanceledException` **hereda de**
`OperationCanceledException` — pensado para dejar propagar una cancelación real de shutdown,
terminó también dejando propagar un timeout de HTTP que no tiene nada que ver con shutdown. La
excepción escapó de `ProcessNextAsync`, tumbó el `BackgroundService`, y con
`HostOptions.BackgroundServiceExceptionBehavior` en su default (`StopHost`), se cerró **todo el
proceso `SmartDoc.Worker`** sin dejar ningún log de fallo del job en sí. Corregido: el filtro
ahora chequea `!cancellationToken.IsCancellationRequested` — el token real que se pasó al
método, no el tipo de excepción — así que solo una cancelación genuina de *ese* token (apagado
del host) sigue propagándose; cualquier otro `TaskCanceledException` (incluido un timeout de
`HttpClient`) entra al retry granular como cualquier otro fallo transitorio.

**Timeouts de `/embed` subidos de 60s a 600s, en ambos lados de la llamada.** Con el bug
anterior ya corregido, el mismo documento grande seguía agotando sus 4 reintentos contra el
mismo timeout de 60s — el fix de arriba evita que tumbe el proceso, pero no alcanza si el
timeout en sí es más corto que lo que tarda un embed real. Medido con el corpus real: ~600-700
ms/chunk contra Ollama local, así que un documento con cientos de chunks necesita varios
minutos, no segundos. Subido en las dos puntas de la cadena: `AiServiceClient`'s
`HttpClient.Timeout` (.NET → `ai-service`, `DependencyInjection.cs`) y el `httpx.AsyncClient`
timeout dentro de `OllamaEmbeddingProvider` (`ai-service` → Ollama) — ambos a 600s, para que
ninguno de los dos tramos sea el que corta corto una llamada lenta-pero-viva.

## Consecuencias

- Los tres documentos afectados (`Get Hired with AI.pdf`, `MemoriasDeUnIngeniero.pdf`, el
  manual de Fortinet de 272 páginas) se reprocesaron con los fixes aplicados y llegaron a
  `Ready` sin intervención manual — verificado con el `Worker` real corriendo, no solo con
  tests.
- Verificado además llamando directo a `ai-service` (`httpx`, fuera de .NET) con los dos PDFs
  problemáticos antes de dar el fix por bueno, para aislar la causa del lado de Python primero.
- `pytest` (19 tests) y `dotnet test` de `SmartDoc.UnitTests` (65 tests) sin cambios, ambos en
  verde después de los fixes — ninguno cubría estos casos antes (gap de test coverage real,
  no evaluado en profundidad en esta sesión más allá de notarlo).
- **Pendiente, fuera de este ADR — anotado explícitamente para resolver antes de cerrar Fase 5:**
  un `ProcessingJob` que queda en estado `Running` por un cierre abrupto del `Worker` (crash,
  kill, corte de luz) no tiene ningún mecanismo de recuperación — `ProcessingJobPollingWorker`
  solo recoge jobs `Pending`, así que un job huérfano así queda parado para siempre sin
  reintentar y sin loguear nada. Encontrado en el camino (el documento de 399 páginas quedó en
  este estado tras el crash que motivó el fix de arriba) y resuelto manualmente por esa vez
  (reset a `Pending` vía SQL directo), pero el gap de fondo — política de qué se considera
  "stale" y dónde vive ese chequeo (¿al arrancar el `Worker`? ¿un chequeo periódico?) — no se
  encaró todavía.
