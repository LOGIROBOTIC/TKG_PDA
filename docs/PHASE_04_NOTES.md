# TKG PDA Phase 04 — Authenticated Backend API

**Status:** Implemented on `agent/phase-04-authenticated-api`; awaiting draft-PR review
**Runtime:** ASP.NET Core / .NET 10, PostgreSQL 18.3
**Scope:** HTTP API, authentication, RBAC, machine permissions, device context and audit only

Phase 04 exposes the Phase 03 workflow engine through an authenticated HTTP boundary. It contains no Admin UI, PDA UI, RDS/SEER client, PLC or SAP integration. HTTP handlers never update `ops.transport_requests`, `ops.slot_runtime_state` or `ops.machine_runtime_state` directly; create, cancel and variant-switch mutations call `TransportWorkflowService`.

## Phase 03 merge baseline

Accepted PR #1 was merged into `main` before Phase 04 began. The merge commit and Phase 04 base SHA are `f854e4f3d9b56674952ebf9ebc505b4e49aa475a`.

## Project structure

| Project/path | Responsibility |
|---|---|
| `src/TkgPda.Api` | ASP.NET Core host, JWT/device middleware, policies, error mapping and HTTP endpoints |
| `src/TkgPda.Application/ApiModels.cs` | Framework-independent auth, permission, operational-context and audit records |
| `src/TkgPda.Application/IApiDataStore.cs` | Read/auth/audit persistence port; it contains no workflow-state mutation method |
| `src/TkgPda.Infrastructure.Postgres/PostgresApiDataStore.cs` | PostgreSQL auth/query/audit adapter |
| `tests/TkgPda.Auth.Tests` | PBKDF2 and JWT security tests |
| `tests/TkgPda.Api.Tests` | Real PostgreSQL authenticated HTTP integration/concurrency tests |
| `tests/phase_04/run_postgres_api_integration.ps1` | Fresh PostgreSQL 18.3 API test runner |

The API uses `Microsoft.AspNetCore.Authentication.JwtBearer` and `Microsoft.AspNetCore.Mvc.Testing` 10.0.10, aligned with the installed .NET 10.0.10 runtime.

## Actual auth and device schema

No auth schema was inferred. The accepted migration contains:

| Table | Relevant fields |
|---|---|
| `auth.roles` | `id`, unique `code`, `name`, `is_system`, `is_active`, `created_at` |
| `auth.users` | `id`, unique `username`, `password_hash`, `full_name`, `employee_no`, `is_active`, `last_login_at`, timestamps |
| `auth.user_roles` | composite `user_id + role_id`, `assigned_at` |
| `auth.user_machine_permissions` | composite `user_id + machine_id`, `can_view`, `can_operate`, `can_change_variant`, `can_manage_slots` |
| `audit.client_devices` | unique `device_code`, `name`, `device_type`, `is_active`, `metadata_json`, timestamps |
| `audit.audit_logs` | actor/device, action/entity, before/after JSON, IP, user agent, correlation ID, timestamp |

The exact seeded role codes are:

```text
ADMIN
SUPERVISOR
LINE_OPERATOR
WAREHOUSE_OPERATOR
```

No application user or default password is committed. User provisioning remains an explicit later configuration function.

## Password hashing

`Pbkdf2PasswordHasher` uses:

- PBKDF2-HMAC-SHA512
- 210,000 iterations
- random 16-byte cryptographic salt
- 32-byte derived key
- fixed-time hash comparison
- self-describing versioned format: `$tkg-pbkdf2-sha512$v=1$i=210000$<salt>$<hash>`
- bounded iteration parsing to prevent pathological stored values
- a dummy hash verification path for unknown usernames to reduce user-enumeration timing differences

Malformed and unsupported hashes fail closed. Passwords and hashes are never written to logs, tokens or audit JSON.

## JWT design

- Algorithm: HS256
- Issuer: `tkg-pda` by default
- Audience: `tkg-pda-clients` by default
- Lifetime: 15 minutes
- Clock skew: 30 seconds
- Signing key: configuration/environment only; startup fails when it is shorter than 32 UTF-8 bytes
- Claims: `sub`, `unique_name`, `jti`, `full_name`, repeated role claims, `device_id`, `device_code`
- Refresh tokens: not implemented because the accepted schema has no refresh/session table

Every authenticated request must repeat the token-bound `X-Device-Code`. The middleware requires an exact claim/header match and rechecks that the device is still active in PostgreSQL.

## Authorization matrix

`ADMIN` bypasses per-machine permission rows. Every other role requires the current permission bit from PostgreSQL.

| Endpoint | Role policy | Machine permission |
|---|---|---|
| `POST /api/v1/auth/login` | anonymous | active registered device required |
| `GET /api/v1/auth/me` | any of the four known roles | none |
| `GET /api/v1/machines` | any known role | list filtered by `can_view`; ADMIN sees all active masters |
| `GET /api/v1/machines/{id}/operational-context` | any known role | `can_view` |
| `GET /api/v1/transport-requests/{id}` | any known role | `can_view` for request machine |
| `POST /api/v1/transport-requests` | ADMIN/SUPERVISOR/LINE_OPERATOR/WAREHOUSE_OPERATOR | `can_operate` |
| `POST /api/v1/transport-requests/{id}/cancel` | ADMIN/SUPERVISOR/LINE_OPERATOR/WAREHOUSE_OPERATOR | `can_operate` for request machine |
| `POST /api/v1/machines/{id}/variant-selection` | ADMIN/SUPERVISOR | `can_change_variant` |
| `/health/live`, `/health/ready` | anonymous | none |

`can_manage_slots` is discovered and returned where relevant but no slot-management endpoint exists in Phase 04.

## Operational-context response

The machine operational-context endpoint returns:

- machine identity/display order and effective permission bits
- active variant, layout and optional navigation-profile identity
- machine-runtime row version and update time
- all active variant/published-layout assignment options, including shared layouts
- active-layout slots with display role, enabled state and normalized 0–1 geometry
- slot runtime occupancy/load, current part/container codes and row version
- explicit `transportEligible`

The 11 layout rows with role `MACHINE` remain visible for drawing but return `transportEligible = false`. They do not change the customer machine master count of 9.

## Transport HTTP behavior

Creation requires an `Idempotency-Key` header. The authenticated user supplies no actor/origin fields: the API writes `requested_by` from JWT `sub` and fixes origin to `API`.

- First successful request: `201 Created`, `Idempotency-Replayed: false`, `Location` header
- Same key and equivalent payload: `200 OK`, existing request, `Idempotency-Replayed: true`
- Same key and different payload: `409`, `IDEMPOTENCY_KEY_REUSED`
- Slot race: one `201`, one `409 SLOT_ALREADY_RESERVED`
- Cancel is available only through the Phase 03 workflow service
- `SENT_TO_RDS`/`RUNNING` cancel: `409 CANCELLATION_AFTER_EXECUTION_DEFERRED`

There is deliberately no HTTP route for dispatch, running, completion or failure. A future trusted RDS adapter receives a separate internal integration boundary.

## Variant-switch behavior

The endpoint requires both an ADMIN/SUPERVISOR role and `can_change_variant`, except for the ADMIN permission-row bypass. `TransportWorkflowService.SwitchVariantAsync` still enforces the same-machine published assignment and active-reservation guard. Navigation profile remains optional.

## Error mapping

Errors use `application/problem+json` with stable `code` and `correlationId` extensions.

| HTTP | Examples |
|---:|---|
| 400 | malformed login/request, missing idempotency/device context, invalid operation |
| 401 | missing/invalid JWT, invalid credentials, invalid token subject |
| 403 | role denial, machine permission denial, inactive/mismatched device |
| 404 | machine/request/slot/part/container/task type not found |
| 409 | state/context conflict, reservation conflict, idempotency reuse, late cancellation |
| 422 | disabled/cross-machine/cross-layout/MACHINE-region endpoint |
| 429 | partitioned login rate limit |
| 503 | readiness/database unavailable |
| 500 | sanitized unexpected error; implementation detail only in server logs |

## Device and audit behavior

- Login requires `X-Device-Code` referencing an active `audit.client_devices` row.
- JWT is bound to that device ID/code.
- Every authenticated request repeats the same header and revalidates active state.
- Login success/failure, request create/replay/cancel and variant switch write audit rows.
- Audit records actor, device, action/entity, IP, user agent and correlation ID.
- `X-Correlation-ID` accepts a valid supplied UUID or the API generates one and returns it.

## Health endpoints

- `/health/live`: process liveness; no database query
- `/health/ready`: executes `SELECT 1`; returns `200 ready` or `503 not-ready`

## Security controls

- Required 256-bit-or-greater JWT key material; no fallback secret
- 15-minute bearer tokens with issuer/audience/signature/lifetime validation
- Device-bound tokens and active-device revalidation
- PBKDF2 fixed-time verification and generic invalid-credential response
- Login fixed-window rate limit: 10 attempts/minute per IP/device partition
- Exact known-role default authorization policy
- Current machine permission lookup on every machine-scoped operation
- 64 KiB request-body limit
- `nosniff`, frame denial and no-referrer headers
- Auth/problem responses use `Cache-Control: no-store`
- No CORS policy is opened in this backend-only phase
- Sanitized error responses; password/token values are not logged
- Audit trail for security and workflow entry points

## Schema gaps and deferred controls

No Phase 04 blocker or migration requirement was found. The following are explicit later concerns:

- no refresh-token/session/revocation table; short-lived JWT requires re-login
- no persistent failed-login counter or distributed lockout; current rate limiter is process-local
- no user-to-device assignment table; any active registered device may authenticate a valid user
- no password history, password-changed timestamp or separate algorithm column; algorithm/version/iterations are encoded in `password_hash`
- no initial-user provisioning API; this belongs to the later Admin Configuration API

These gaps must be revisited before production rollout or horizontal API scaling. They do not justify changing the frozen baseline migration in Phase 04.

## Validation

```powershell
dotnet build TKG.Pda.slnx --configuration Release
dotnet test tests/TkgPda.Auth.Tests --configuration Release
pwsh tests/phase_04/run_postgres_api_integration.ps1
pwsh tests/phase_03/run_postgres_integration.ps1
python -m unittest discover -s tests/phase_02a -p "test_*.py" -v
python -m unittest discover -s tests/phase_02b -p "test_*.py" -v
```

| Suite | Result |
|---|---:|
| Release build | PASS — 0 warnings, 0 errors |
| Authentication tests | PASS — 11/11 |
| Authenticated HTTP/PostgreSQL tests | PASS — 19/19 on PostgreSQL 18.3 |
| Phase 03 PostgreSQL workflow/concurrency | PASS — 20/20 |
| Phase 02A regression | PASS — 10/10 |
| Phase 02B regression | PASS — 13/13 |

The HTTP suite proves two concurrent requests for one slot yield exactly one reservation and proves concurrent same-key retries yield one logical request. It finishes with 9 machine masters, the exact four roles, and zero RDS task/worksite rows.

## Local startup

The API requires secrets through configuration; no `.env` or credential file is committed:

```powershell
$env:ConnectionStrings__TkgPda = 'Host=127.0.0.1;Database=tkg_pda;Username=...;Password=...'
$env:Authentication__Jwt__SigningKey = '<at least 32 UTF-8 bytes from a secret store>'
dotnet run --project src/TkgPda.Api
```

Phase 04 stops here. Admin UI, PDA UI, RDS, SEER navigation, PLC and SAP have not started.
