# ADR 0017 — JWT auth: hasheo de passwords, emisión/validación de tokens, endpoints protegidos

**Status:** Aceptado

## Contexto

Fase 5 (Production polish) arranca con el punto pendiente más señalado desde ADR 0008:
implementar login real y dejar de confiar en un `UserId` que el cliente manda sin validar.
Hasta ahora, `Documents`/`Chat`/`Search` tomaban `UserId` del body/form del request — cualquiera
podía actuar "a nombre de" cualquier usuario existente con solo cambiar ese valor.

## Decisiones

**Passwords hasheados en la tabla `Users` desde ahora, no solo en config.** Existía una
alternativa más simple para una PoC de un solo seed user — un password fijo en
`appsettings`/env var, comparado en texto plano — pero se descartó a favor de guardar un hash
real (`PasswordHash` en `Users`, columna `NOT NULL` sin `HasMaxLength` porque el formato PBKDF2
de `PasswordHasher<T>` puede cambiar de longitud entre versiones). Más cercano a producción real
sin costo adicional relevante a esta escala.

**`Microsoft.AspNetCore.Identity`'s `PasswordHasher<User>` (PBKDF2), no una implementación
propia.** Es el mismo mecanismo que usaría un `IdentityUser` real, sin traer el resto del
paquete de ASP.NET Core Identity (roles, lockout, tokens de reset, etc. — no aplican a un
único seed user y violarían el scope guard de `CLAUDE.md`). Solo se toma
`Microsoft.Extensions.Identity.Core` como dependencia.

**El seeder resincroniza el hash en cada arranque de la Api, no solo la primera vez.**
`SmartDocDbContextSeeder.SeedAsync` ahora hashea `Jwt:SeedUserPassword` y lo aplica sobre el
usuario existente si ya existe (antes solo insertaba una vez). Esto evita que cambiar la
password en config quede "trabado" por un hash viejo en la DB — para un único seed user de
desarrollo, resincronizar en cada arranque es más simple que un flujo de "cambiar password".

**`JwtTokenGenerator`: HMAC-SHA256, claims mínimos (`sub`, `email`, `jti`), sin issuer/audience.**
`sub` = `User.Id` (lo único que los endpoints necesitan para derivar `UserId`), `email` solo
informativo, `jti` (un GUID por token) para tener un identificador único de token aunque no se
use todavía para revocación. Sin `iss`/`aud` — no hay múltiples issuers ni audiences distintas
en este proyecto; agregarlos sería configuración sin nada real que validar. Expiración
configurable (`Jwt:ExpirationMinutes`, default 60).

**`MapInboundClaims = false` en `AddJwtBearer`.** Sin este flag, ASP.NET Core remapea
automáticamente los nombres cortos de claims JWT estándar (`sub`, `email`) a URIs largas del
namespace de WS-Federation/SOAP (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/...`)
al construir el `ClaimsPrincipal` — un comportamiento heredado que sorprende si no se conoce:
el claim `sub` que el generador escribe no se encuentra buscando `"sub"` en el principal
resultante si no se desactiva. `ClaimsPrincipalExtensions.GetUserId()` depende de que el nombre
del claim se preserve tal cual se firmó.

**Mismo `401 Unauthorized` para "email no existe" y "password incorrecta" en `POST
/api/auth/login`.** No distinguir el caso evita que el endpoint sirva para enumerar emails
registrados probando cuáles no dan el mismo error.

**`Documents` sigue siendo una base de conocimiento compartida; `Conversations` pasa a ser
personal.** Decisión ya anticipada en la discusión de ADR 0006 (documentos de un usuario
visibles/consultables por otros en un escenario "empresa de 200 empleados"), ahora hecha
explícita en código: `DocumentEndpoints` no filtra por `UserId` del caller en `GET`/`DELETE`
(cualquier usuario autenticado opera sobre cualquier documento). `ChatEndpoints.
GetConversationAsync`, en cambio, exige `c.UserId == userId` — una conversación es del usuario
que la inició, no del "conocimiento común" — y devuelve `404` (no `403`) tanto si la
conversación no existe como si es de otro usuario, sin distinguir los dos casos.

**El chequeo "¿existe este `UserId`?" se eliminó de `DocumentEndpoints`/`ChatEndpoints`, no se
dejó como defensa redundante.** Tenía sentido cuando `UserId` era un valor arbitrario del
cliente (ADR 0008); una vez que sale de un JWT validado por el propio servidor, ya no hay forma
de que sea un GUID inexistente sin forjar la firma — mantenerlo sería código muerto que nunca
se ejecuta, no una capa extra de seguridad real.

## Consecuencias

- Migration `AddPasswordHashToUsers`: columna `NOT NULL` con `defaultValue: ""` para no romper
  la fila del seed user ya existente — se resincroniza sola en el próximo arranque de la Api
  vía el seeder.
- `POST /api/documents`, `POST/GET /api/chat`, `POST /api/search` ahora requieren
  `Authorization: Bearer <token>` (`RequireAuthorization()` en sus route groups); piden `401`
  sin token o con uno inválido/expirado.
- `CreateDocumentRequest` y `ChatRequest` ya no reciben `UserId` del cliente — sale de
  `ClaimsPrincipal.GetUserId()`.
- 17 tests nuevos entre unit e integration (hasheo/validación de `User`, `POST /api/auth/login`
  con las 5 combinaciones válido/inválido, `401` sin token en los tres endpoints protegidos,
  aislamiento de `Conversations` entre usuarios). `AuthTestHelper.AuthenticateAs` mintea tokens
  directamente vía `JwtTokenGenerator` resuelto del contenedor de DI de
  `WebApplicationFactory`, evitando repetir el flujo real de login en cada test de otros
  endpoints (ya cubierto una vez, de punta a punta, en `AuthEndpointsTests`). 108 tests totales.
- Verificado además con un smoke test manual de punta a punta con la Api real corriendo:
  `GET /api/documents` sin token → `401`; `POST /api/auth/login` con password incorrecta →
  `401`; login correcto → token; `GET /api/documents` con ese token → `200`.
- Pendiente, fuera de este ADR: no hay revocación de tokens (el `jti` se emite pero no se
  persiste ni se chequea contra ninguna lista); aceptable para un solo seed user sin logout
  real en el MVP. Endpoints de `Users` (registro, cambio de password) siguen fuera de scope
  (ADR 0007) — el único usuario sigue siendo el seed, gestionado por config.
