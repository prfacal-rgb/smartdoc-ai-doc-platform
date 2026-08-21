# SmartDoc frontend

React + Vite + TypeScript + Tailwind CSS dashboard for SmartDoc — upload PDFs, watch them
process, and ask questions about them with cited answers. See the root
[`README.md`](../README.md) and [`docs/architecture.md`](../docs/architecture.md) for the
overall project; see [ADR 0025](../docs/decisions/0025-frontend-react-vite-tailwind.md) for
the choices made building this specifically.

## Development

Requires the Api running and reachable (default: `http://localhost:8080`, e.g. via
`docker compose up -d` from the repo root — see the root README's
[Getting started](../README.md#getting-started)).

```bash
npm install
npm run dev       # http://localhost:5173
```

Point at a different Api instance by copying `.env.example` to `.env.local` and setting
`VITE_API_BASE_URL`.

```bash
npm run build      # type-checks (tsc -b) then builds to dist/
npm run lint        # oxlint
```

## Structure

```
src/
├── api/           # HTTP client + types mirroring the Api's DTOs 1:1
├── auth/          # AuthContext — JWT in localStorage
├── hooks/         # useDocuments (polls while a document is processing), useChat
└── components/    # LoginScreen, DocumentsPanel, UploadBox, ChatPanel, StatusBadge
```

Single-screen dashboard: `DocumentsPanel` (shared document list, top-left) + `UploadBox`
(drag-and-drop) on the left, `ChatPanel` (question box + cited answers, one continuous
conversation per session) on the right. Requires signing in first — the Api requires a JWT
on every endpoint except `/api/auth/login` and `/health`.
