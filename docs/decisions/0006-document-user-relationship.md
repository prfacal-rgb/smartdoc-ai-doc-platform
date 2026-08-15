# ADR 0006 — Relación Document ↔ User y borrado de usuarios

**Status:** Aceptado

## Contexto

`Document.UserId` se agregó desde el principio como columna e índice, pero sin Foreign Key
en la configuración de EF Core (`DocumentConfiguration`) — un review posterior detectó que
esto permite persistir un `Document` con un `UserId` que no existe en `Users`, sin que nada
lo impida a nivel de base de datos. Hacía falta decidir tres cosas relacionadas: si la FK
lleva navigation properties, qué pasa con los `Documents` de un `User` que se borra, y si
"borrar un usuario" debería ser un borrado físico o lógico.

## Decisión

**FK sin navigation properties.** `DocumentConfiguration` declara la relación con
`HasOne<User>().WithMany().HasForeignKey(d => d.UserId)`, sin agregar `Document.User` ni
`User.Documents` a las entidades de dominio. `User` y `Document` quedan como aggregates
independientes — no hay necesidad concreta hoy de navegar de uno a otro vía EF (ver
`CLAUDE.md`: "no abstraer/acoplar por costumbre").

**El borrado de `User` es lógico, no físico.** No se borra nunca una fila de `Users`;
se marca como inactiva. Esto es intencional pensando en la evolución del proyecto más allá
del seed user único del MVP (ver discusión de contexto más abajo) — un borrado físico de
usuarios que suben documentos es, en general, indeseable (se pierde auditoría de "quién
subió qué", y complica qué hacer con sus `Documents`).

**La FK usa `DeleteBehavior.Restrict`, no `Cascade`.** Esto es una red de seguridad, no el
mecanismo principal: dado que el borrado esperado de `User` es lógico, la FK casi nunca
debería ejercitarse por un `DELETE` real. `Restrict` existe para que, si alguna vez ocurre
un borrado físico no previsto (script de admin, job de limpieza, bug), Postgres lo rechace
en vez de arrastrar en cascada los `Documents` del usuario silenciosamente.

**La implementación del borrado lógico (`DeletedAt`/`IsDeleted` en `User`, método
`SoftDelete()`) queda diferida.** Se documenta la decisión ahora, pero el código no se
escribe todavía: no existe ningún endpoint de borrado de usuario en esta fase, y agregar esa
pieza sin un caso de uso real ejercitándola sería especulativo. Se implementa cuando exista
el primer flujo que la necesite.

## Consecuencias

- La base de datos ahora rechaza un `Document` cuyo `UserId` no exista en `Users`
  (verificado con un integration test).
- Migration nueva requerida para agregar la FK sobre el schema ya aplicado.
- Queda pendiente, para cuando se implemente el borrado de usuario: agregar
  `DeletedAt`/`IsDeleted` a `User`, un método explícito `SoftDelete()` (siguiendo el mismo
  patrón de métodos de transición ya usado en `Document`), y un global query filter en
  `UserConfiguration` que excluya usuarios borrados por defecto.
- Si en el futuro aparece una necesidad concreta de navegar `user.Documents` (por ejemplo,
  una query que lo justifique de forma recurrente), se agrega la navigation property en ese
  momento — no antes.
