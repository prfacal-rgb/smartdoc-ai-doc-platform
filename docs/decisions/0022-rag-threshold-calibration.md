# ADR 0022 — Calibración empírica de `Rag:MaxRelevantDistance`

**Status:** Aceptado

## Contexto

`Rag:MaxRelevantDistance` (ADR 0016) arrancó en `0.75` como valor puesto a ojo, explícitamente
marcado como pendiente de ajuste empírico contra tráfico real — `PROJECT.md` lo describe como
"configurable", y ADR 0020 instrumentó el logging de `Distance` crudo específicamente para
poder hacer esta calibración cuando llegara el momento. Este ADR documenta la metodología
usada y el resultado.

## Metodología

**Corpus:** 6 PDFs variados en tema y longitud, elegidos para que hubiera diversidad temática
real (necesaria para poder construir preguntas negativas creíbles): gestión ágil (16p),
resumen de un libro de Python (31p), guía de búsqueda de empleo con IA (59p), una novela
satírica de oficina (94p), una ficha comercial de un resort (33p), y un manual técnico de
configuración de Fortinet (272p). Un séptimo PDF (399 páginas) se excluyó del corpus final —
ver ADR 0021 — por exceder el throughput práctico de embeddings incluso con los timeouts ya
corregidos; no aportaba diversidad temática que los otros seis no cubrieran ya.

**45 preguntas de calibración**, con ground truth armado a mano leyendo contenido real de cada
PDF (no generado sintéticamente):
- **24 positivas directas** — respuesta literal/cercana al texto de un documento y página
  conocidos.
- **12 positivas parafraseadas** — mismo hecho que una de las directas, reformulado con
  sinónimos/estructura distinta, para testear si el embedding aguanta paráfrasis y no solo
  match léxico.
- **9 negativas** — preguntas sobre temas que ningún PDF del corpus cubre (cocina, historia
  medieval, deportes, jardinería, astronomía, salud, química, impuestos), para definir el
  "piso": ninguna debería producir una distancia razonablemente baja.

**Herramienta:** `POST /api/search` en vez de `POST /api/chat` — devuelve el `Distance` crudo
de cada candidato sin aplicar el threshold y sin llamar a `/generate` (Groq), así que correr
las 45 preguntas no cuesta nada y no depende de parsear logs después. `scripts/calibrate-rag-
threshold.ps1` (PowerShell, no versionado como parte de la app pero sí trackeado en el repo por
ser una herramienta reutilizable) automatiza login → subida de los PDFs → poll de
`Document.Status` hasta `Ready` → corrida de las 45 preguntas → volcado de resultados a JSON.
Escrito para Windows PowerShell 5.1 específicamente (confirmado con `$PSVersionTable` en la VM
de desarrollo) — `Invoke-RestMethod -Form` no existe en esa versión (recién en PowerShell 6+),
así que el upload multipart usa `HttpClient` (.NET) directo desde el script en su lugar.
`calibration/` (PDFs, preguntas, resultados crudos) queda gitignoreada — son insumos de
trabajo, no artefactos del proyecto; este ADR es el registro durable.

## Resultado

Con el threshold actual (`0.75`), **ninguna de las 45 preguntas quedaba filtrada** — la
distancia máxima observada en todo el corpus fue `0.52`. En la práctica, el fallback de
"insufficient context" que `PROJECT.md` §7 pedía (ADR 0016) era código muerto: cualquier
pregunta, incluidas las 9 completamente ajenas al corpus, le llegaba a `/generate` con
contexto irrelevante en vez de recibir la respuesta de "no tengo información".

Comparando la distancia mínima (mejor candidato) de cada pregunta positiva vs. cada negativa:

| threshold | positivas respondidas (de 36) | negativas respondidas mal (de 9) |
|---|---|---|
| 0.20 | 3 | 0 |
| 0.30 | 29 | 0 |
| 0.32 | 33 | 0 |
| 0.34 | 34 | 1 |
| 0.40 | 35 | 4 |
| 0.75 (anterior) | 36 | 9 |

Entre `0.312` (última positiva aceptada por debajo de ese punto) y `0.336` (primer empate entre
una positiva y la negativa más difícil, "¿cómo podar un rosal?") hay un hueco vacío sin ningún
dato — cualquier valor ahí adentro clasifica las 45 preguntas exactamente igual.

**Elegido: `0.33`.** Cero falsos positivos entre las 9 negativas, 33/36 (91.7%) de recall en
las positivas. Decisión deliberada de priorizar precisión sobre recall: en un RAG que vende
citas como garantía de que la respuesta viene de un documento real, una respuesta confiada
construida sobre contexto irrelevante es peor que un "no tengo información" de más — el
usuario puede reformular, pero una cita falsa mina la confianza en la feature entera.

**Caso límite encontrado, no accionado:** de las 36 positivas, una sola (pregunta sobre el
título de una sección, contra el índice/tabla de contenidos de `AgileProjectManagement.pdf`)
tuvo su mejor match del documento correcto en `0.48`, con el candidato top-1 real viniendo de
otro archivo — la única de las 36 donde el mejor match global no fue del documento esperado.
Contenido de tabla de contenidos es estructuralmente poco semántico (títulos cortos, números de
página) y el embedding lo retrieval peor que prosa normal. Queda como una limitación conocida
del retrieval para ese tipo de contenido, no un problema del threshold en sí — no se
investigó más a fondo en esta sesión.

## Consecuencias

- `Rag:MaxRelevantDistance` actualizado de `0.75` a `0.33` en
  `SmartDoc.Api/appsettings.Development.json` (único lugar donde estaba seteado
  explícitamente) y en el default del fallback en código
  (`ChatEndpoints.cs`, `configuration.GetValue("Rag:MaxRelevantDistance", 0.33)`) — para que
  un ambiente sin la key explícita en su `appsettings.json` (hoy solo existe en Development) no
  caiga silenciosamente de vuelta al valor viejo sin calibrar.
- Ningún test dependía del valor anterior — `dotnet test` (65 unit tests) sin cambios.
- Sin cambios en `SearchEndpoints` (no aplica threshold, por diseño — ADR 0016) ni en
  `SimilaritySearchService` (el threshold se aplica después de la búsqueda, no en el SQL).
- El corpus de calibración se dejó cargado en la base de datos de desarrollo (`Documents`/
  `DocumentChunks`) en vez de borrarlo — a diferencia de los datos sintéticos de ADR 0019, acá
  son documentos reales y legítimos; sirven como corpus de prueba real para `/api/chat` manual
  o una demo, no son descartables. `DocumentChunks` deja de estar en 0 filas a partir de esta
  sesión.
