# ADR 0026 — Modelo de embeddings multilingüe: `nomic-embed-text` → `bge-m3`

**Status:** Aceptado

## Contexto

Uso real durante Fase 6 (frontend) expuso un problema de fondo: una pregunta en español
("¿cuáles son los cambios para lograr madurez operacional en IA?") sobre un PDF en inglés
recién subido devolvió "no tengo información", citando un capítulo de una novela sin
relación (`MemoriasDeUnIngeniero.pdf`). Diagnosticado con las mismas herramientas de ADR
0022 (`/api/search` sin pasar por el LLM, distancias crudas) contra 4 pares
documento/idioma independientes:

| Documento (idioma real) | Query EN | Query ES |
|---|---|---|
| GenAI paper (EN) | 0.196 ✅ | no aparece en el top 50 ❌ |
| Manual Fortinet (EN) | 0.265 ✅ | no aparece ❌ (gana un documento sin relación) |
| Resort SECPM (EN) | 0.329 ✅ | no aparece ❌ |
| Automatiza con Python (ES) | 0.396 ⚠️ (sigue #1, cruza el threshold) | 0.164 ✅ |

Patrón sistemático y asimétrico: `nomic-embed-text` (mayormente inglés) no alinea bien el
espacio semántico español↔inglés — una pregunta en español termina compitiendo solo contra
chunks en español por similitud de superficie/idioma, sin importar el tema real. (De paso,
un hallazgo lateral: `SECPM-RPG.pdf` no es el documento de seguridad en español que se
asumía al armar el primer test — es una ficha de un resort, en inglés; corregido en el
camino antes de sacar conclusiones equivocadas.)

## Decisiones

**`bge-m3` (BAAI), no `paraphrase-multilingual`.** Entrenado específicamente para retrieval
denso multilingüe/cross-lingual sobre 100+ idiomas — `nomic-embed-text` es mayormente
inglés. 600M parámetros vs. 137M, 1024 dimensiones vs. 768, 1.2GB de descarga vs. 274MB —
más pesado, pero a la escala de este proyecto (un modelo cargado a la vez, sin tráfico
concurrente) el costo es aceptable a cambio de retrieval que funciona de verdad.

**Migration de esquema, no solo config.** `DocumentChunk.EmbeddingDimensions` (768 → 1024)
es un invariante de dominio, no solo del schema (ADR 0013) — cambiarlo requiere migration.
La migration generada por `dotnet ef migrations add` no alcanza sola: Postgres no puede
reinterpretar un `vector(768)` existente como `vector(1024)` (dimensiones distintas no son
casteables entre sí), y tampoco puede alterar una columna de la que depende un índice HNSW
mientras ese índice existe. Migration editada a mano: dropear el índice → `DELETE FROM
"DocumentChunks"` (los embeddings existentes son del modelo que se está reemplazando, no
hay nada que conservar) → `ALTER COLUMN TYPE vector(1024)` → recrear el índice HNSW sobre
la tabla ahora vacía (mismo razonamiento que ADR 0019 — HNSW construye incrementalmente,
una tabla vacía es el punto de partida esperado, no un problema).

**Reprocesar todo el corpus reusando el pipeline existente, no un script de re-embed
ad-hoc.** Los 7 documentos existentes se resetean a `Uploaded` con un `ProcessingJob`
`Pending` nuevo cada uno (SQL directo, no un endpoint nuevo — es un evento único, no un
patrón que valga productizar) y el `Worker` los reprocesa de punta a punta
(parse→chunk→embed) con el pipeline real, en vez de escribir un camino paralelo
"solo-reembeder" que duplicaría lógica ya probada.

**Timeout de `/embed` subido de 600s a 1800s (.NET → ai-service → Ollama, ambas puntas),
medido, no adivinado — mismo criterio que ADR 0021.** Primer intento de reproceso: el
manual Fortinet (409 chunks) agotó los 4 intentos y quedó `Failed`, siempre con
`TaskCanceledException` a los ~600s exactos. Medición directa (parse+chunk reales vía
ai-service, embed directo contra Ollama con un cliente sin timeout propio) confirmó la
causa: `bge-m3` con chunks reales de ~500 tokens tardó **750s (12.5 min) para ese único
documento**, ~1835ms/chunk — bastante más lento que la estimación inicial con texto
sintético más corto (773ms/chunk) y ~2.6-3x más lento que `nomic-embed-text` en la misma
comparación (~600-700ms/chunk, ADR 0021). 1800s da margen real (~2.4x) sobre el documento
más grande del corpus actual sin necesidad de trocear el batch — la filosofía ya
establecida en ADR 0021/DependencyInjection.cs ("nada en el tráfico de este proyecto es
sensible a latencia") sigue aplicando.

**Recalibración completa de `Rag:MaxRelevantDistance` (ADR 0022), de `0.33` a `0.5`, no un
ajuste cosmético.** `bge-m3` produce una distribución de distancias distinta —
sistemáticamente más altas incluso para matches correctos— así que el valor viejo no tenía
sentido reusarlo sin remedir. Corpus de calibración ampliado de 45 a 53 preguntas:
- 3 preguntas nuevas (2 directas + 1 parafraseada) para el 7º documento del corpus (el PDF
  de GenAI subido durante el diagnóstico), que no tenía cobertura en el set original.
- 4 preguntas cross-lingual (los mismos pares usados para diagnosticar el bug), para que la
  recalibración *valide el fix explícitamente*, no solo lo asuma.
- 1 pregunta negativa abstracta/filosófica ("What is the meaning of life?") — encontrada
  porque ya existía como aserción fija en `ChatEndpointsTests`, y expuso un hueco real: las
  9 negativas originales eran todas fácticas/concretas (recetas, geografía, biología),
  ninguna abierta. `bge-m3` puso un pasaje de `MemoriasDeUnIngeniero.pdf` (una rutina de
  darle de comer a un bebé) a distancia ~0.52 para esa pregunta — un falso positivo real que
  las negativas concretas no exponían.

Con las 53 preguntas, no existe un único threshold con 100% recall y cero falsos positivos
a la vez (a diferencia de antes de agregar la negativa abstracta) — mismo trade-off
explícito de ADR 0022, priorizando precisión: **`0.5`** da cero falsos positivos en las 10
negativas con 95.3% de recall en las 43 positivas (41/43) — mejor recall que el 91.7%
original, con el mismo criterio de "una cita falsa cuesta más que un 'no tengo información'
de más". Las 2 preguntas que quedan fuera a esta distancia son lookups puntuales de
estructura de un solo documento (título de una sección del índice, nombre de un concepto
específico) — el mismo tipo de miss de borde que ADR 0022 ya aceptaba.

**Encontrado y corregido de paso: `$PSScriptRoot` vacío en los defaults del bloque
`param()` de `calibrate-rag-threshold.ps1`.** Bug preexistente, no introducido en esta
ronda — quirk conocido de Windows PowerShell 5.1 con `[CmdletBinding()]` (arreglado en
PowerShell 7/`pwsh`, no instalado en esta máquina): `$PSScriptRoot` no está poblado todavía
mientras se evalúan los valores default del `param()` propio del script, así que
`"$PSScriptRoot/../calibration/questions.json"` resolvía a `/../calibration/questions.json`
— sin el prefijo del directorio, terminando en `C:\calibration\questions.json`. Corregido
resolviendo esos tres paths en el cuerpo del script, después del `param()`, en vez de en
los defaults.

## Consecuencias

- `DocumentChunk.EmbeddingDimensions` 768 → 1024; migration
  `SwapEmbeddingModelToBgeM3` (editada a mano) aplicada contra la base real.
- `ai-service`: `OLLAMA_EMBEDDING_MODEL` default `bge-m3`; `.env` local (para `uvicorn
  --reload` suelto, gitignored) también actualizado — tenía el valor viejo hardcodeado y
  rompía los tests de `/embed` corridos localmente contra el Ollama real.
- Timeout de `/embed` (.NET `AiServiceClient` y `ai-service`'s `httpx`) 600s → 1800s.
- `Rag:MaxRelevantDistance` 0.33 → 0.5 en `appsettings.Development.json`, `docker-
  compose.yml`, `.env.example`, y el fallback en código de `ChatEndpoints.cs`.
- `calibration/questions.json`: 45 → 53 preguntas (gitignored, no versionado — igual que
  con ADR 0022).
- 2 tests actualizados: 1 aserción .NET (`EmbeddingModel == "nomic-embed-text"` →
  `"bge-m3"`), 2 aserciones Python (768 → 1024 dim, nombre de modelo). Un tercer test
  (`PostChat_WithNoRelevantDocuments_ReturnsInsufficientContextAndNoSources`) que fallaba
  por el mismo motivo que motivó la pregunta negativa abstracta arriba se corrigió solo,
  sin tocar su código, una vez recalibrado el threshold — la aserción del test era correcta,
  el `0.55` intermedio que se había probado antes de encontrar este caso no lo era.
- 122 tests .NET (65 unit + 57 integración) y 23 de `ai-service` en verde contra el stack
  real (Postgres, MinIO, `ai-service` reconstruido, Ollama real con `bge-m3`).
- Verificado de punta a punta con la pregunta real que disparó todo esto: `POST /api/chat`
  con "¿cuáles son los cambios para lograr madurez operacional en IA?" contra el stack real,
  contenedorizado, ahora responde citando correctamente `18956083-dz-tr-genai-2026.pdf`
  (páginas 31-34, la sección "Five Shifts Toward Operational Maturity") en vez de devolver
  "insuficiente contexto" citando una novela sin relación.
