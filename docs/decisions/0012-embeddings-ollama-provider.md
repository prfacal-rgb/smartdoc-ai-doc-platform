# ADR 0012 — `/embed` con Ollama, conectividad confirmada

**Status:** Aceptado

## Contexto

Con `/parse` y `/chunk` funcionando (ADR 0011), faltaba `/embed` — pendiente de confirmar
que el contenedor `ai-service` efectivamente alcanza el Ollama de la máquina física antes de
escribir el cliente (discutido en la ronda de decisión de proveedor de embeddings: Ollama
elegido sobre OpenAI/Gemini para evitar límites de cuenta gratuita y sin auto-fallback entre
proveedores, por contradecir el scope guard de `CLAUDE.md`).

## Verificado

- Conectividad confirmada desde dentro del contenedor: `192.168.56.1:11434` responde
  (`docker exec smartdoc-ai-service` + `urllib.request` contra `/api/tags`).
- `nomic-embed-text:latest` ya pulleado.
- Endpoint OpenAI-compatible de Ollama (`/v1/embeddings`) probado directamente: **768
  dimensiones**, **0.66s** para un texto individual, **0.14s** para un batch de 3 — mucho más
  rápido que la preocupación inicial de "muy lento", que era sobre los modelos de 14B/32B
  (chat/código), no sobre un modelo de embeddings dedicado (correctamente dimensionado:
  cientos de MB, no decenas de GB).
- **Soporta batch nativo** (`input` como lista) — un solo request para varios chunks, no hace
  falta loopear texto por texto.

## Decisiones

**`EmbeddingProvider` (ABC) en `app/embeddings/provider.py`** — el equivalente Python de
`IEmbeddingProvider` (`CLAUDE.md` §3). `.NET` nunca habla con Ollama directamente; solo llama
a `POST /embed` de este servicio, que delega al proveedor configurado.

**`OllamaEmbeddingProvider`** como única implementación por ahora, seleccionada vía
`EMBEDDING_PROVIDER` (config, no hardcodeada). Agregar OpenAI/Gemini más adelante es una
clase nueva implementando la misma interfaz, sin tocar el router ni el lado .NET.

**La respuesta de `/embed` incluye `model` explícitamente** (no asumido por el caller) —
esto es lo que permite al lado .NET completar `DocumentChunk.EmbeddingModel` por-chunk sin
inferir nada de su propia config, tal como se pidió al discutir el diseño de `DocumentChunks`.

**Errores del proveedor upstream devuelven `502`, no `500` ni `400`** — la request a
`/embed` en sí fue válida, pero la dependencia externa (Ollama) falló; distinción útil para
cuando `ProcessingJobProcessor` decida cómo tratar el fallo (Fase 3, próximo paso).

## Consecuencias

- 4 tests nuevos: 2 reales contra el Ollama físico (texto único, batch), 1 de validación
  (`texts` vacío → 422), 1 con `dependency_overrides` de FastAPI para simular una falla del
  proveedor sin depender de la red real — mismo criterio que los tests de "unit" vs.
  "integration" ya usados del lado .NET.
- Verificado además con el contenedor Docker real reconstruido y corriendo (no solo el venv
  local ni pytest): `/embed` end-to-end vía PowerShell + `HttpClient`.
- `.env` local (gitignored) agregado para que el venv de desarrollo directo en la VM también
  apunte a `192.168.56.1:11434` — sin esto, `pytest` corrido fuera de Docker usa el default
  `localhost:11434`, que no llega a nada desde la VM.
- Próximo paso: `DocumentChunks` (entidad .NET + columna `vector(768)`) y el wiring real en
  `ProcessingJobProcessor` que reemplaza el placeholder de Fase 2 — llamando efectivamente a
  `/parse` → `/chunk` → `/embed` y persistiendo los chunks con su `EmbeddingModel`.
