# ADR 0015 — `Conversations`/`Messages`

**Status:** Aceptado

## Contexto

Siguiente pieza de Fase 4: el schema de `PROJECT.md` §6 para persistir el historial de chat
(`Conversations: Id, UserId, CreatedAt` / `Messages: Id, ConversationId, Role, Content,
CreatedAt`).

## Decisiones

**Las citas se guardan como parte de `Content`, no en una tabla separada.** El schema
documentado en `PROJECT.md` no tiene una columna/tabla de citas — solo `Content`. La
respuesta completa del asistente (prosa + sección `"Sources:"`, armada del lado .NET según
ADR 0014) se persiste tal cual se le devuelve al usuario. Evita agregar una tabla
`Citations` sin que `PROJECT.md` la pida — si en algún momento hace falta re-renderizar
citas de forma estructurada (no solo texto), se agrega en ese momento, no antes.

**`Role` como enum (`User`/`Assistant`), no string libre.** Mismo patrón que `DocumentStatus`
y `ProcessingJobStatus` — persistido como string legible (`HasConversion<string>()`), no
número.

**FKs siguiendo los precedentes ya establecidos**, no decisiones nuevas: `Conversation.UserId
→ Users.Id` con `Restrict` (mismo motivo que `Document → User`, ADR 0006 — `User` tiene valor
de auditoría independiente). `Message.ConversationId → Conversations.Id` con `Cascade` (mismo
motivo que `ProcessingJob → Document`, ADR 0009 — un mensaje no tiene sentido sin su
conversación).

## Consecuencias

- Migration `AddConversationsAndMessages` aplicada.
- 12 tests nuevos: unit tests de ambas entidades, y persistencia (round-trip, cascade delete
  de `Messages`, y verificado explícitamente que el `Restrict` de `Conversation → User` lo
  aplica Postgres — no solo la detección client-side de EF Core, que tira una excepción
  distinta si ambas entidades ya están trackeadas en el mismo `DbContext`).
- Próximo: similarity search contra `pgvector` desde .NET (con el índice pendiente desde
  ADR 0004) y los endpoints `POST /api/search`/`POST /api/chat`/`GET
  /api/chat/{conversationId}` que finalmente conectan retrieval + `/generate` + citas.
