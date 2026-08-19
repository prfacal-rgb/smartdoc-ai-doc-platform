# ADR 0024 — Docker Compose completo: `Api`/`Worker` contenedorizados

**Status:** Aceptado

## Contexto

`docker-compose.yml` solo tenía `postgres`/`minio`/`ai-service` — `SmartDoc.Api` y
`SmartDoc.Worker` corrían sueltos vía `dotnet run` durante toda la sesión de desarrollo (así
corrió, de hecho, todo el trabajo de ADR 0021-0023). `PROJECT.md` lista "Docker Compose" como
entregable del MVP, y `docker compose up -d` como comando único de bootstrap solo tiene sentido
si de verdad levanta todo, no una parte. Dockerizar esto obligó a resolver, de paso, algo que
ADR 0004 había dejado explícitamente pendiente desde Fase 1: "si `dotnet ef database update` se
corre manualmente en dev o se automatiza al levantar el Worker/Api — no se ha tomado esa
decisión todavía."

## Decisiones

**Migrations automáticas, solo desde la `Api`, no desde el `Worker`.** Resuelve el pendiente de
ADR 0004: `db.Database.MigrateAsync()` se agregó al arranque de `Program.cs`, antes del seed de
usuario/bucket ya existente. Solo la `Api` lo hace — si las dos también lo hicieran, arrancarían
casi simultáneamente vía `docker compose up -d` y podrían correr `Migrate()` en paralelo contra
una base recién creada, un escenario real de carrera en DDL. En cambio, `worker` tiene
`depends_on: api: condition: service_healthy` — el `Worker` no arranca hasta que la `Api` ya
migró y quedó sirviendo, sin necesidad de que el `Worker` sepa nada de migrations.

**`ASPNETCORE_ENVIRONMENT=Development` también en el contenedor, no `Production`.** Lo único
que ese valor gatilla hoy es Scalar UI (`/scalar/v1`, ADR 0007). Este proyecto es una PoC de
portfolio pensada para que un revisor la explore, no un deploy real con usuarios que proteger —
tener Scalar disponible desde el contenedor vale más que la semántica de "production" que no
aplica acá. Documentado explícitamente para que no se lea como un descuido.

**Nueva imagen de healthcheck para la `Api` (`GET /health`), simétrica a la de `ai-service`.**
Kestrel no empieza a aceptar conexiones hasta `app.Run()`, que corre después del bloque de
migration/seed — así que el healthcheck no puede reportar "healthy" antes de que ese bloque
termine, lo cual es exactamente la garantía que `worker`'s `depends_on` necesita.

**Secretos del `.env` con el mismo valor que ya estaba público en
`appsettings.Development.json`, no una rotación nueva.** `JWT_SECRET` en `.env.example` reusa
el secreto que ya vive commiteado en el repo desde ADR 0017 — no hay nada más sensible que
proteger acá, y mantener el mismo valor evita que `docker compose up -d` deje de ser un
bootstrap de un solo comando por tener que pedirle al usuario que complete un secreto real
antes de arrancar (mismo criterio que ya usan `POSTGRES_PASSWORD`/`MINIO_ROOT_PASSWORD`, no el
que usa `GROQ_API_KEY`, que sí requiere una cuenta real).

**Los contenedores de `Api`/`Worker` no leen `appsettings.Development.json` (pensado para
`dotnet run` en el host) — toda la config viene de variables de entorno en
`docker-compose.yml`.** `Host=localhost` no resuelve dentro de la red de Docker; los
contenedores necesitan `Host=postgres`, `http://minio:9000`, `http://ai-service:8000` (nombres
de servicio). En vez de mantener un tercer archivo `appsettings.Docker.json` en paralelo, se
usa la precedencia estándar de configuración de ASP.NET Core (variables de entorno pisan
`appsettings*.json`) para reconstruir cada valor desde las mismas variables de `.env` que ya
usan `postgres`/`minio` (`POSTGRES_USER`, `MINIO_ROOT_USER`, etc.) — evita que alguien cambie
`POSTGRES_PASSWORD` y se olvide de actualizarlo en un segundo lugar.

**Un solo paso de `dotnet publish`, no `restore` separado de `build --no-restore`.** El primer
intento separaba ambos pasos (patrón común para cachear la capa de Docker) — se topó con
`NETSDK1064` ("AWSSDK.S3 ... was not found") en el `publish`, pese a que el `restore` inmediato
anterior había reportado éxito para el mismo proyecto. No se investigó la causa raíz a fondo
(huele a un problema conocido de NuGet con restores parciales entre pasos); se optó por lo más
simple y confiable — un solo `dotnet publish` (restore implícito) — sacrificando algo de
velocidad de rebuild por robustez, razonable para una PoC sin pipeline de CI que dependa de ese
cacheo.

**`runtime:10.0` para el `Worker`, `aspnet:10.0` para la `Api`.** El `Worker` es un Generic Host
puro (ni Kestrel ni ASP.NET Core), así que la imagen más liviana alcanza. Encontrado en el
camino: esa imagen más chica no trae `libgssapi-krb5-2`, que Npgsql intenta cargar al negociar
la conexión aunque el proyecto use auth por password plano, no Kerberos — sin ella, Npgsql
igual conecta (falla el intento de GSS y sigue) pero loguea un error feo en cada conexión.
Agregada explícitamente vía `apt-get` en el Dockerfile del `Worker` (la imagen `aspnet` que usa
la `Api` ya la trae por defecto, por eso ese contenedor nunca mostró el problema).

## Consecuencias

- `db.Database.MigrateAsync()` nuevo en `SmartDoc.Api/Program.cs` — corre también contra
  Postgres cuando se levanta vía `dotnet run` en el host (no es exclusivo de Docker), sin
  cambiar nada ahí porque la base local ya estaba migrada.
- `GET /health` nuevo en la `Api`, sin autenticación — mismo criterio que `ai-service`, no
  expone nada sensible.
- `backend-dotnet/.dockerignore` nuevo (`bin/`, `obj/`, `.vs/`, `*.user`).
- Verificado end-to-end con el stack real completo, no solo `docker compose config`: `docker
  compose up -d` levanta las 5 servicios en el orden esperado (`postgres`/`minio`/`ai-service`
  healthy → `api` healthy recién después de migrar+seedear → `worker` arranca solo entonces);
  login, listado de los 6 documentos del corpus de calibración (ADR 0022, intactos), y un
  upload nuevo de punta a punta (`Uploaded` → `Ready` procesado por el `Worker` contenedorizado)
  confirmados contra la Api real corriendo en contenedor, no contra `dotnet run`.
- 65 unit + 57 integration tests (.NET) sin cambios — corridos contra el mismo Postgres/MinIO/
  ai-service que ahora también sirven a los contenedores de `Api`/`Worker`, sin conflicto (los
  tests de integración usan `localhost:5432`/`:9000`/`:8000`, publicados igual que antes; `api`
  ocupa el puerto 8080 nuevo, `worker` no publica ningún puerto).
