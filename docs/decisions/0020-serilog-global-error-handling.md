# ADR 0020 — Serilog real + manejo global de excepciones + logging de `Distance` crudo

**Status:** Aceptado

## Contexto

Tres gaps de "production polish" (Fase 5) se cierran juntos porque están relacionados: sin
logging estructurado real, cualquier excepción no manejada en la Api llegaba al cliente como
un 500 vacío (o la dev exception page en Development) sin quedar registrada de forma
consistente; y sin ver la distancia cruda que devuelve `SimilaritySearchService` antes del
filtro de `Rag:MaxRelevantDistance` (ADR 0016), no hay forma de generar la señal necesaria
para ajustar ese threshold con tráfico real.

**Hallazgo en el camino:** `Serilog.AspNetCore` ya estaba referenciado en
`SmartDoc.Api.csproj` desde el arranque del proyecto (`CLAUDE.md` lo declara en el stack),
pero nunca se llamó a `UseSerilog` — la Api corrió toda Fase 1-4 sobre el logger de consola
por defecto de ASP.NET Core, no sobre Serilog. `SmartDoc.Worker` no tenía ninguna referencia a
Serilog.

## Decisiones

**Serilog conectado en ambos procesos (`Api` y `Worker`), no solo en la Api.**
`ProcessingJobProcessor`/`ProcessingJobPollingWorker` ya tenían buen logging estructurado vía
`ILogger` (intentos de retry, fallos permanentes — ADR 0018); el cambio es de *sink*, no de
disciplina de logging, que ya era correcta. `Serilog.AspNetCore` trae `Serilog.Sinks.Console`
de forma transitiva para la Api; `Worker` (Generic Host puro, sin ASP.NET Core) necesitó
`Serilog.Extensions.Hosting` + `Serilog.Sinks.Console` + `Serilog.Settings.Configuration`
explícitos.

**Configuración vía appsettings (`ReadFrom.Configuration`), no hardcodeada en código.**
Consistente con cómo ya se manejan otros tunables del proyecto (`Rag:*`, `Worker:*`) — una
sección `Serilog` en `appsettings.json` de cada proyecto, sin duplicar en
`appsettings.Development.json` porque no hay nada distinto que overridear todavía.

**Consola + archivo, no solo consola.** Primer intento: sink de consola únicamente, con el
argumento de que "`docker logs` ya captura stdout". Ese argumento resultó **incorrecto**:
`Api` y `Worker` no corren containerizados (`docker-compose.yml` solo tiene `postgres`,
`minio` y `ai-service` — `Api`/`Worker` corren sueltos vía `dotnet run`, ver "Comandos de
desarrollo" en `CLAUDE.md`), así que no hay ningún `docker logs` capturando nada de ellos —
sin sink de archivo, el log de `Distance` (la razón misma de este ADR) se pierde apenas se
cierra la terminal. Corregido agregando un sink de `File` (rolling diario,
`retainedFileCountLimit: 14`) junto al de consola.

**Archivo = JSON estructurado (`CompactJsonFormatter`), consola = texto legible.** Mismo
criterio en los tres procesos (`Api`, `Worker`, `ai-service`): la consola prioriza lectura en
vivo durante desarrollo, el archivo prioriza ser parseable/queryable después sin reprocesar
texto libre. `CompactJsonFormatter` (formato CLEF) ya venía disponible transitivamente vía
`Serilog.AspNetCore` para la Api; `Worker` necesitó `Serilog.Sinks.File` +
`Serilog.Formatting.Compact` explícitos (igual que con `Serilog.Sinks.Console` antes).

**`Distance` en archivo propio (`logs/distance-.log`), además del log general.** Un
sub-logger de Serilog (código, no config — el filtro por `SourceContext` no se expresa bien
en JSON) captura únicamente los eventos de `RagDistanceLog.CategoryName`
(`SmartDoc.Api.Rag.Distance`) y los escribe también a un archivo aislado
(`retainedFileCountLimit: 30`, más retención que el log general porque es justamente el dato
que se necesita conservar para calibrar `Rag:MaxRelevantDistance`). Sigue apareciendo también
en `logs/api-.log` — no se pierde contexto, solo se agrega un segundo destino filtrado.
`ChatEndpoints`/`SearchEndpoints` loguean con `loggerFactory.CreateLogger(RagDistanceLog.
CategoryName)` en vez de un logger por-clase, porque hoy es lo único que loguean.

**`ai-service` (Python) — mismo criterio, sin dependencia nueva.** `JsonFormatter`
(`app/logging_config.py`) es un `logging.Formatter` estándar de la librería stdlib —
`@t`/`@l`/`logger`/`message`/`exception` más cualquier `extra={...}` de la llamada — no un
CLEF estricto, pero la misma convención visual para que un archivo de cualquiera de los dos
lados se lea igual. Aplicado vía `--log-config` de uvicorn (`app/logging_config.json`,
`logging.config.dictConfig`), no `logging.basicConfig()` a nivel de módulo — uvicorn configura
sus propios loggers (`uvicorn`/`uvicorn.access`/`uvicorn.error`) al arrancar, y hacerlo a nivel
de import hubiera corrido el riesgo de una carrera donde esa configuración posterior pisara los
handlers ya puestos. `--log-config` le da a un solo `dictConfig` el control de todo, loggers de
uvicorn incluidos, sin orden ambiguo. Como `ai-service` sí corre containerizado,
`docker-compose.yml` monta `./ai-service-python/logs:/app/logs` para que el archivo sea visible
desde el host sin `docker exec`/`docker cp`.

**`IExceptionHandler` (patrón nativo de .NET 8+), no middleware artesanal.**
`GlobalExceptionHandler` (`SmartDoc.Api`) registrado vía `AddExceptionHandler<T>()` +
`AddProblemDetails()` + `app.UseExceptionHandler()`. La lógica de mapeo
(`GlobalExceptionHandler.MapException`, `static`) vive separada del resto de la clase
específicamente para poder testearla sin necesitar un host real ni un
`IProblemDetailsService` real — mismo criterio de "extraer la decisión para que sea testeable
sin la maquinaria" que ya se usó con `ProcessingJobProcessor` (ADR 0009/0013).

**Mapeo deliberadamente angosto: `HttpRequestException` → 503, todo lo demás → 500
genérico.** La única dependencia externa que la Api llama de forma síncrona en el request path
es `AiServiceClient` (`EnsureSuccessStatusCode()` lanza `HttpRequestException` si
ai-service/Groq/Ollama no responden o devuelven error) — es el único modo de falla real y
distinguible que existe hoy. Ampliar el mapeo a otros tipos de excepción sin un segundo caso
real que lo justifique sería adivinar. El mensaje del 500 genérico nunca incluye
`exception.Message` — puede filtrar detalle interno (connection strings, paths); el mensaje
real va al log, no a la respuesta.

**`Distance` crudo logueado antes del filtro de threshold, en `ChatEndpoints` y
`SearchEndpoints`.** `matches` ya contenía el `Distance` de cada candidato, pero se descartaba
al aplicar `.Where(m => m.Distance <= maxRelevantDistance)` — el dato existía, no se
persistía en ningún lado observable. Ahora se loguea como propiedad estructurada
(`{@Distances}`, Serilog) antes de filtrar, junto con cuántos candidatos entraron dentro del
threshold — la señal que ADR 0016 dejó pendiente para calibrar
`Rag:MaxRelevantDistance` empíricamente, sin esperar a instrumentar nada nuevo cuando llegue
el momento de hacerlo.

## Consecuencias

- `GlobalExceptionHandlerTests` (unit, sin host): cubre el mapeo de excepciones directamente.
- `ChatEndpointsTests.PostChat_WhenAiServiceIsUnreachable_ReturnsServiceUnavailableProblemDetails`
  (integración, pipeline real): reemplaza `IAiServiceClient` por un doble que lanza
  `HttpRequestException` (`WithWebHostBuilder(...).ConfigureServices(...)`, última
  registración gana sobre el `AddHttpClient<IAiServiceClient, AiServiceClient>()` real de
  `Program.cs`) para forzar esa excepción a través del middleware completo y verificar el
  `ProblemDetails` 503 resultante. Se intentó primero apuntar `AiService:BaseUrl` a un puerto
  loopback sin listener vía `ConfigureAppConfiguration` — no funcionó de forma confiable (la
  petición terminaba llegando al ai-service real), así que se cambió a reemplazar el servicio
  directamente, más robusto y no dependiente del timing exacto del host builder.
- `ChatEndpoints`/`SearchEndpoints` pasan a recibir `ILoggerFactory` en vez de `ILogger<T>` —
  ambas son clases estáticas (Minimal API, ADR 0003) y `ILogger<ChatEndpoints>` no compila
  (`CS0718`, los tipos estáticos no pueden ser argumento de tipo genérico); se resuelve con
  `loggerFactory.CreateLogger(typeof(ChatEndpoints))`.
- `logs/` (en `SmartDoc.Api/`, `SmartDoc.Worker/`, y `ai-service-python/`) agregado a
  `.gitignore` — son artefactos locales, no versionados.
- Verificado con los tres procesos corriendo de verdad (no solo `dotnet test`): `dotnet run`
  de `Api`/`Worker` generando `logs/api-*.log`, `logs/distance-*.log` y `logs/worker-*.log`
  con JSON válido y bien filtrado; `ai-service` reconstruido y corriendo en Docker con el
  volumen montado, `logs/ai-service.log` visible en el host con JSON limpio (se encontró y
  corrigió en el camino un campo `asctime` que se filtraba como ruido — efecto colateral de
  que Python comparte el mismo `LogRecord` entre handlers, no un dato real).
- Pendiente, fuera de este ADR: el ajuste empírico de `Rag:MaxRelevantDistance` en sí — este
  ADR agrega el instrumento, no corre la calibración (requiere generar tráfico real primero).
