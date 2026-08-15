# ADR 0008 — Diferir JWT auth a Fase 5

**Status:** Aceptado

## Contexto

Ni `PROJECT.md` ni `CLAUDE.md` atan la implementación de JWT auth a ninguna fase numerada de
la tabla de fases — aparece solo como bullet genérico de scope del MVP y como "known
limitation" sugerida para el README. Al cerrar los endpoints CRUD de `Documents` (ADR 0007)
esta laguna se volvió concreta: `CreateDocumentRequest.UserId` es un campo que pone el
cliente en el body, sin nada que lo valide contra un usuario autenticado — cualquiera puede
crear un `Document` "a nombre de" cualquier `UserId` existente con solo cambiar ese valor.

## Decisión

Se difiere la implementación de JWT auth (login del seed user, `[Authorize]` en los
endpoints, `UserId` derivado del token) hasta la **Fase 5 (Production polish)**, en vez de
construirla ahora junto con los primeros endpoints. Prioriza avanzar hacia Async processing
/ AI pipeline / RAG — la parte más distintiva del proyecto para portfolio — por sobre cerrar
una superficie de auth que hoy es chica (4 endpoints) pero que puede cambiar de forma a
medida que aparecen Fase 2-4 (job/worker, endpoints de search/chat), evitando retrofitear
login dos veces.

**Riesgo interino aceptado y documentado:** mientras tanto, cualquier endpoint que reciba un
`UserId` (hoy `Documents`, y los que se agreguen en Fases 2-4) lo recibe del cliente sin
validar identidad. Es aceptable para un entorno de desarrollo local sin exposición pública,
pero debe quedar explícito como limitación conocida — no es un descuido.

## Consecuencias

- Ningún endpoint de Fases 2-4 va a requerir `[Authorize]` ni token — siguen el mismo patrón
  de `UserId` explícito en el request que ya usa `Documents`.
- `Jwt:Secret` y `Jwt:SeedUserPassword` (documentadas en `CLAUDE.md` como env vars
  esperadas) siguen sin usarse hasta Fase 5.
- README del portfolio (Fase 5, sección "Known limitations" per `PROJECT.md` §10) debe
  explicitar esto como decisión consciente, no como carencia: *"Auth was intentionally
  deferred to focus on the async processing / RAG pipeline first; endpoints trust a
  client-supplied UserId during development."*
- Al empezar Fase 5, el primer paso de seguridad es implementar el login + proteger
  retroactivamente todos los endpoints construidos hasta ese momento (Documents, y los que
  se sumen en Fases 2-4), reemplazando `UserId` del body por el claim del JWT.
