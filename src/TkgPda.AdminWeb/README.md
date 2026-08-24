# TKG PDA Admin Web

React + TypeScript + Vite client for the Phase 05 administration API. See [Phase 06 documentation](../../docs/PHASE_06_ADMIN_WEB.md) for architecture, routes, security and validation details.

```powershell
npm ci
npm run dev
```

The development server listens on `http://127.0.0.1:4173` and proxies `/api` and `/health` to `TKG_API_TARGET` or `http://localhost:5000`.

```powershell
npm run typecheck
npm run lint
npm run test:run
npm run test:e2e
npm run build
```

Do not add `.env` files, credentials, `node_modules`, `dist` or test artifacts to Git. The normalized editor is not a robot map and must never be used to infer robot coordinates or routes.
