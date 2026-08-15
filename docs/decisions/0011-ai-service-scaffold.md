# ADR 0011 — Scaffold de `ai-service-python`: `/parse` y `/chunk`

**Status:** Aceptado

## Contexto

Primer código del servicio Python (`CLAUDE.md` §Estructura del repo). Se arranca con
`/parse` y `/chunk` — no `/embed` todavía (pendiente de confirmar conectividad real desde el
contenedor hacia el Ollama de la máquina física) ni `/generate` (Fase 4, RAG).

## Decisiones

**FastAPI + pydantic-settings**, siguiendo el stack ya fijado en `CLAUDE.md`. Config leída de
env vars con defaults razonables (`app/config.py`).

**`pypdf` para extracción de texto** (no PyMuPDF/fitz): licencia permisiva (BSD), sin
dependencias de sistema, suficiente para PDFs con texto real. **Sin OCR** — PDFs escaneados
(solo imagen) van a devolver texto vacío o casi vacío por página; es una limitación conocida
del MVP, coherente con "PDF únicamente" de `PROJECT.md` §8, no algo a resolver ahora.

**Chunking por página, no a través de todo el documento.** Cada chunk queda asociado a
exactamente un `page_number` — necesario para el formato de cita "archivo — página N" que
`PROJECT.md` exige como requisito obligatorio (§7). El costo: un chunk cerca de un salto de
página no incluye contexto de la página siguiente/anterior. Sliding window con overlap
configurable; defaults `chunk_size_tokens=500`, `overlap_tokens=75` (dentro del rango
50-100 que sugiere `PROJECT.md`).

**`tiktoken` (`cl100k_base`) como conteo de tokens aproximado, no exacto.** Ningún modelo de
embeddings de los que se van a usar (`nomic-embed-text`, o eventualmente OpenAI) tokeniza
exactamente así, pero es un estándar de facto ampliamente usado como estimador de "cuántos
tokens es este texto" independiente del proveedor — documentado explícitamente como
aproximación, no como medición exacta.

**`fpdf2` como dependencia de test** para generar PDFs reales en los fixtures
(`tests/conftest.py`), en vez de armar bytes de PDF a mano (frágil: offsets de xref
incorrectos rompen fácilmente) o commitear archivos binarios de prueba al repo.

**Un solo `requirements.txt`** (incluye `pytest`/`fpdf2`, no hay `requirements-dev.txt`
separado) — coherente con el comando ya documentado en `CLAUDE.md`
(`pip install -r requirements.txt`). Costo aceptado: la imagen Docker de producción instala
dependencias de test que no usa en runtime; se revisita en Fase 5 si el tamaño de imagen
llega a importar.

## Consecuencias

- 11 tests (`pytest`) cubriendo parsing (páginas múltiples, tipo de archivo inválido, PDF
  corrupto, archivo vacío) y chunking (chunks múltiples, no cruzar páginas, overlap inválido,
  defaults).
- `ai-service` activado en `docker-compose.yml` (ya no comentado) con healthcheck propio
  (`GET /health`).
- Verificado con el contenedor real construido y corriendo (no solo el venv local): `/health`,
  `/parse` con un PDF real de 2 páginas, y `/chunk` con overlap — probado con PowerShell +
  `HttpClient` (`curl` no disponible en este entorno).
- `/embed` queda pendiente del próximo paso: confirmar que el contenedor alcanza
  `192.168.56.1:11434` (Ollama en la máquina física) antes de escribir el cliente.
