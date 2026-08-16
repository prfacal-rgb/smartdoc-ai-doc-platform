# ADR 0014 — Proveedor de LLM para `/generate`: Groq

**Status:** Aceptado

## Contexto

Arrancando Fase 4 (RAG), hacía falta elegir proveedor de LLM para `/generate`. A diferencia
de `/embed` (Fase 3), esta llamada es **síncrona y user-facing** — `POST /api/chat` espera la
respuesta en tiempo real, no corre en background como el Worker. Eso invalida el argumento
que hizo aceptable la lentitud de Ollama local para embeddings.

## Medido

- **Ollama local, `qwen2.5-coder:14b`**: 40.67s para una respuesta de 35 tokens
  (~0.87 tokens/s). Con un prompt real de RAG (contexto de varios chunks + pregunta,
  fácilmente 500-2000+ tokens de entrada) y una respuesta de varios cientos de tokens, esto
  proyecta a varios minutos por pregunta — inviable para un endpoint síncrono.
- **Groq, `llama-3.3-70b-versatile`**: 0.47s totales, `completion_time: 0.106s` para
  35 tokens (~330 tokens/s) — ~380x más rápido que Ollama local en esta máquina.
- Groq es gratuito (free tier) — mantiene la misma lógica de costo-consciencia que ya se
  usó para elegir Ollama en embeddings, ahora que Ollama queda descartado por latencia.

## Decisión

**Groq (`llama-3.3-70b-versatile`) para `/generate`**, descartando tanto Ollama local (muy
lento para uso síncrono) como Anthropic Sonnet/Haiku (pagos — el usuario tiene cuenta pero
rompe la narrativa de costo del proyecto, que ya no tenía sentido mantener "Anthropic +
Ollama" tal como sugería `PROJECT.md` original una vez confirmado que Ollama no sirve para
generación). Sin auto-fallback entre proveedores (mismo criterio que embeddings — contradice
el scope guard de `CLAUDE.md`).

**`LlmProvider` (ABC) en `app/llm/provider.py`** — equivalente Python de `ILlmProvider`
(`CLAUDE.md` §3), mismo patrón que `EmbeddingProvider` (ADR 0012). Agregar otro proveedor
(Anthropic Haiku como plan B si el free tier de Groq resulta insuficiente) es una clase
nueva, sin tocar el router.

**Citas NO delegadas al LLM.** `/generate` recibe `question` + `context_chunks` (texto
plano, sin metadata de archivo/página) y responde solo el texto de la respuesta. El lado
.NET ya sabe exactamente qué chunks recuperó (con su `FileName`/`PageNumber`) y arma la
sección "Sources:" de forma determinística desde esa metadata — no confía en que el LLM
reproduzca citas correctamente en su propio texto (evita alucinación de citas).

**Prompt del sistema instruye explícitamente "no sé" si el contexto no alcanza** — defensa
en profundidad además del similarity threshold que .NET aplica antes de siquiera llamar a
`/generate` (si ningún chunk pasa el threshold, .NET no llama al LLM y devuelve
"insuficiente contexto" directo). El threshold cubre "no hay nada relevante"; la instrucción
del prompt cubre "hay algo tópicamente similar pero no responde la pregunta puntual".

## Problema encontrado y resuelto

La API de Groq está detrás de Cloudflare, que rechazó el primer request de prueba
(`error code: 1010`) por no llevar un `User-Agent` normal — bloqueo de firma de bot, no un
error de la API ni de la key. Resuelto seteando un `User-Agent` explícito en
`GroqLlmProvider`.

## Consecuencias

- `GROQ_API_KEY` agregada a `.env`/`.env.example` (raíz y `ai-service-python/`) y a
  `docker-compose.yml` (sin default — no hay placeholder seguro para una API key).
- 4 tests nuevos (`pytest`): respuesta real contra Groq usando el contexto dado (verifica
  que no alucina), respuesta sin contexto, validación de pregunta vacía, y fallo del
  proveedor simulado vía `dependency_overrides` (mismo patrón que `test_embeddings.py`).
- Verificado además con el contenedor Docker real reconstruido (variables de entorno
  resueltas correctamente desde el `.env` de la raíz vía `docker-compose.yml`).
- Próximo: similarity search en .NET contra `pgvector` (con el índice pendiente desde
  ADR 0004), `Conversations`/`Messages`, y los endpoints `POST /api/search`/`POST
  /api/chat`/`GET /api/chat/{conversationId}`.
