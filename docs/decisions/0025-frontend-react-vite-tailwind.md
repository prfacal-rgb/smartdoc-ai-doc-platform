# ADR 0025 — Frontend: React + Vite + TypeScript + Tailwind, CORS en la Api

**Status:** Aceptado

## Contexto

Fase 6 (Frontend) arranca con un pedido concreto: un dashboard de una sola pantalla con caja
de subida de documentos, listado de documentos ya subidos arriba a la izquierda, caja de
texto para preguntas y un espacio para las respuestas. `CLAUDE.md` fija React como stack pero
deja explícitamente "a evaluar" el resto (`frontend/` recién se crea en esta fase, no antes).
El backend ya expone todo lo necesario sin cambios de contrato: `POST /api/auth/login`,
`POST`/`GET`/`DELETE /api/documents`, `POST /api/chat` — la única pieza que faltaba del lado
del backend era CORS, nunca necesario hasta ahora porque nada consumía la Api desde un origen
de browser distinto.

## Decisiones

**React + Vite + TypeScript + Tailwind CSS v4, sin librería de estado/data-fetching
adicional.** Vite (no Create React App, discontinuado) es el scaffolding estándar actual.
TypeScript porque el resto del proyecto ya es fuertemente tipado (.NET, Pydantic) — no tiene
sentido bajar la guardia justo en la capa que consume esos contratos. Tailwind v4 vía
`@tailwindcss/vite` (un plugin, sin `tailwind.config.js`/PostCSS separado — la v4 lo resuelve
por convención). Sin Redux/Zustand/TanStack Query: cuatro llamadas HTTP y dos hooks
(`useDocuments`, `useChat`) con `useState`/`useEffect` alcanzan sin agregar una dependencia
más — mismo criterio de "simple y bien hecho" que el resto del proyecto (ver scope guard de
`CLAUDE.md`).

**Pantalla de login real, no auto-login con credenciales embebidas.** Decisión pedida
explícitamente (no asumida): el backend ya tiene un flujo de auth real (ADR 0017) — ocultarlo
detrás de un auto-login habría escondido esa parte del trabajo *y* obligado a embeber la
password del seed user en el bundle del cliente, legible por cualquiera que abra devtools.
El JWT se guarda en `localStorage` (no hay backend para sesiones de servidor que lo justifique
mejor) y se descarta client-side si `expiresAt` ya pasó, sin esperar a que la Api lo rechace.

**Una sola conversación continua por sesión de browser, no un selector de conversaciones.**
El layout pedido tiene una caja de preguntas y un espacio de respuestas, no una lista de
conversaciones — reutilizar el mismo `conversationId` entre preguntas (el `POST /api/chat`
ya lo soporta, ADR 0015) le da historial dentro de la sesión sin construir una UI que nadie
pidió.

**Polling del listado de documentos, no WebSockets/SSE.** El listado de documentos
(`GET /api/documents`) se repollea cada 4s mientras haya alguno en `Uploaded`/`Processing`, y
se detiene solo cuando todos están en `Ready`/`Failed` — mismo patrón que el propio `Worker`
usa para levantar jobs (ADR 0009), aplicado ahora también en el cliente. Server-Sent Events o
WebSockets serían más eficientes pero agregan infraestructura que esta escala no justifica.

**CORS habilitado en la Api, origen(es) permitidos por config.** `Cors:AllowedOrigins` (string
separado por comas, no un array JSON — así el override por variable de entorno
`Cors__AllowedOrigins` tiene la misma forma que el default de
`appsettings.Development.json`) alimenta una default policy sin `AllowCredentials()`: el JWT
viaja en el header `Authorization`, no en una cookie, así que no hace falta compartir
credenciales entre orígenes. Default: `http://localhost:5173` (Vite dev server).

**`frontend/` fuera del build de Docker Compose por ahora.** Se corre con `npm run dev`
(Vite) contra la Api ya sea contenedorizada (`localhost:8080`, default) o suelta
(`VITE_API_BASE_URL` en `.env.local`). Containerizar el frontend (build estático + Nginx, o
agregarlo al `docker-compose.yml`) queda para cuando el frontend esté más maduro — ahora
mismo iterar con HMR de Vite importa más que un bootstrap de un solo comando que incluya
también el frontend.

## Consecuencias

- `frontend/` nuevo: `src/api/` (cliente HTTP + tipos que reflejan los DTOs de la Api 1:1),
  `src/auth/` (`AuthContext`, JWT en `localStorage`), `src/hooks/` (`useDocuments` con
  polling, `useChat`), `src/components/` (`LoginScreen`, `DocumentsPanel`, `UploadBox`,
  `ChatPanel`, `StatusBadge`).
- `SmartDoc.Api`: `AddCors`/`UseCors` nuevo en `Program.cs`, `Cors:AllowedOrigins` nuevo en
  `appsettings.Development.json`; `docker-compose.yml`/`.env.example` con
  `CORS_ALLOWED_ORIGINS` (default `http://localhost:5173`). Sin cambios de contrato en
  ningún endpoint existente.
- Verificado de punta a punta contra el stack real (no solo `dotnet build`/`tsc`): rebuild +
  recreate del contenedor `api` con el cambio de CORS, header `Access-Control-Allow-Origin`
  confirmado en la respuesta real de `/api/auth/login`, y un smoke test completo
  (login → listar → subir PDF real → poll hasta `Ready` → preguntar → citas correctas)
  ejecutado con las mismas formas de request que usa el cliente (`FormData` con campo
  `file`, JSON camelCase, `Authorization: Bearer`), documento de prueba borrado al terminar.
  Verificación visual en browser pendiente de que la extensión de Claude in Chrome esté
  conectada — el dev server (`npm run dev`, puerto 5173) queda corriendo para que el usuario
  lo confirme visualmente.
- `dotnet build` (Api) y `tsc -b`/`npm run build` (frontend) sin errores; 122 tests .NET y 23
  de `ai-service` sin cambios de comportamiento (ningún endpoint existente cambió su
  contrato).
