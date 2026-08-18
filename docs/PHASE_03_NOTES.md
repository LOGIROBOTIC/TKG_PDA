# TKG PDA Phase 03 — Business Workflow Engine

**Status:** Implemented on `agent/phase-03-workflow-engine`; awaiting draft-PR review
**Runtime:** .NET 10 / PostgreSQL 18.3
**Scope:** Domain, application boundary and transactional PostgreSQL persistence only

Phase 03 models TKG logistics independently from RDS. It does not contain an HTTP API, authentication implementation, UI, RDS client, SEER navigation/profile logic or PLC integration.

## Solution structure

| Project | Responsibility |
|---|---|
| `src/TkgPda.Domain` | SQL-aligned enums, state machine, operation/slot/association rules |
| `src/TkgPda.Application` | framework-independent workflow service and persistence port |
| `src/TkgPda.Infrastructure.Postgres` | transactional PostgreSQL implementation, locking and events |
| `tests/TkgPda.Domain.Tests` | state machine and pure business-rule tests |
| `tests/TkgPda.Application.Tests` | command shape and application-boundary tests |
| `tests/TkgPda.Infrastructure.Postgres.Tests` | real PostgreSQL workflow, idempotency and concurrency tests |

`TKG.Pda.slnx`, `global.json` and `Directory.Build.props` define the .NET 10 solution. Warnings are treated as errors.

## SQL states discovered

The existing `ck_transport_status` constraint permits exactly:

```text
CREATED
VALIDATING
QUEUED
SENT_TO_RDS
RUNNING
COMPLETED
FAILED
CANCELLED
```

`SENT_TO_RDS` is retained because it is an existing SQL state. Phase 03 can record the state through a future-executor application boundary for state-machine verification, but performs no RDS or other HTTP call.

## Legal transition table

| Current state | Legal next states | Phase 03 behavior |
|---|---|---|
| `CREATED` | `VALIDATING`, `CANCELLED` | creation transaction advances immediately to validation |
| `VALIDATING` | `QUEUED`, `FAILED`, `CANCELLED` | valid request becomes queued; failure edge is modeled for validation failure reporting |
| `QUEUED` | `SENT_TO_RDS`, `CANCELLED` | Phase 03 stops here during normal request creation; pre-execution cancel is supported |
| `SENT_TO_RDS` | `RUNNING`, `FAILED` | future executor state; no HTTP implementation |
| `RUNNING` | `COMPLETED`, `FAILED` | successful completion applies final slot state; cancellation is deliberately rejected |
| `COMPLETED` | none | terminal |
| `FAILED` | none | terminal; executor failure reconciliation is deferred |
| `CANCELLED` | none | terminal |

```mermaid
stateDiagram-v2
    [*] --> CREATED
    CREATED --> VALIDATING
    CREATED --> CANCELLED
    VALIDATING --> QUEUED
    VALIDATING --> FAILED
    VALIDATING --> CANCELLED
    QUEUED --> SENT_TO_RDS
    QUEUED --> CANCELLED
    SENT_TO_RDS --> RUNNING
    SENT_TO_RDS --> FAILED
    RUNNING --> COMPLETED
    RUNNING --> FAILED
    COMPLETED --> [*]
    FAILED --> [*]
    CANCELLED --> [*]
```

## Operation rules

| Operation | Required machine-slot state | Reservation | Successful completion |
|---|---|---|---|
| `BRING_EMPTY` | `VACANT + UNKNOWN` | destination represented by active request; slot state remains unchanged | `OCCUPIED + EMPTY` |
| `REMOVE_FULL` | `OCCUPIED + FULL` | source represented by active request; `FULL` is preserved | `VACANT + UNKNOWN` |
| `REMOVE_EMPTY` | `OCCUPIED + EMPTY` | source represented by active request; `EMPTY` is preserved | `VACANT + UNKNOWN` |

For `BRING_EMPTY`, the machine destination is mandatory and the warehouse source is nullable. A request can reach `QUEUED` without inventing a warehouse slot. Optional supplied endpoints still have to belong to the active machine layout and cannot be `MACHINE` regions.

The 11 Phase 02B rows with `slot_role = 'MACHINE'` are rejected for every operation. They remain display/physical references only.

## Cancellation restoration

Cancellation is accepted only from `CREATED`, `VALIDATING` or `QUEUED`:

| Operation | Restored state |
|---|---|
| `BRING_EMPTY` | destination → `VACANT + UNKNOWN`; part/container cleared |
| `REMOVE_FULL` | source → `OCCUPIED + FULL`; current known part/container preserved |
| `REMOVE_EMPTY` | source → `OCCUPIED + EMPTY`; current known part/container preserved |

Because reservation does not mutate occupancy/load, cancellation is deterministic without a reservation snapshot column. Cancellation after dispatch/running is rejected with `CANCELLATION_AFTER_EXECUTION_DEFERRED` and belongs to the future executor/RDS integration.

## Transaction and concurrency model

Request creation is a single PostgreSQL transaction:

1. Acquire a transaction-scoped advisory lock derived from `idempotency_key`.
2. Return the existing request when the same key and payload already exist; reject key reuse with a different payload.
3. Lock `ops.machine_runtime_state` and validate active machine/variant/layout; navigation remains optional.
4. Validate published variant-layout assignment and known master references.
5. Ensure and lock every supplied `ops.slot_runtime_state` row in deterministic UUID order.
6. Reject disabled, cross-machine, wrong-layout and `MACHINE` endpoints.
7. Reject any active request already using a supplied slot.
8. Validate operation state and explicit part/container conflicts.
9. Insert the request and event trail, then advance `CREATED → VALIDATING → QUEUED`.

The slot row lock serializes two concurrent requests for the same slot. The advisory lock serializes concurrent PDA retries using the same idempotency key; `uq_transport_idempotency` remains the database backstop. No new migration is required.

Variant switching locks the same machine runtime row before checking active reservations. Any request in `CREATED`, `VALIDATING`, `QUEUED`, `SENT_TO_RDS` or `RUNNING` blocks the switch. After cancellation/completion, a valid same-machine published assignment can be selected.

## Part and container evidence

- Missing request part/container values are allowed.
- A supplied known master part/container is allowed when the slot evidence is unresolved.
- No relationship or physical footprint is guessed or written back to master data.
- A supplied part conflicting with non-empty source-backed `canonical_part_codes` is rejected.
- A supplied container conflicting with `default_container_type_id` or current known slot state is rejected.
- Unknown master IDs are rejected as impossible references.

## Schema assessment

No blocking schema gap was found and no migration was added.

- Reservation is represented by an active `ops.transport_requests` row rather than a new slot enum value.
- `slot_role`, the idempotency unique index, runtime state and event tables already support the required behavior.
- Warehouse source/destination master data is genuinely absent and therefore remains nullable.
- `navigation_id` remains optional.

The role and active-reservation invariants are guaranteed by the workflow engine transaction. Direct ad-hoc SQL writes to `ops.transport_requests` could bypass these application invariants; future APIs must call this engine rather than inserting requests directly.

## Validation commands

```powershell
dotnet build TKG.Pda.slnx --configuration Release
dotnet test tests/TkgPda.Domain.Tests --configuration Release
dotnet test tests/TkgPda.Application.Tests --configuration Release
pwsh tests/phase_03/run_postgres_integration.ps1

python -m unittest discover -s tests/phase_02a -p "test_*.py" -v
python -m unittest discover -s tests/phase_02b -p "test_*.py" -v
```

The Phase 03 PostgreSQL runner initializes a fresh PostgreSQL cluster, applies `001 + 002a + 002b`, executes real concurrency tests, confirms that the 11 machine regions are still present, and verifies that no RDS task/worksite was created.

## Validation result

| Suite | Result |
|---|---:|
| .NET Release build | PASS — 0 warnings, 0 errors |
| Domain tests | PASS — 34/34 |
| Application tests | PASS — 9/9 |
| PostgreSQL workflow/concurrency tests | PASS — 17/17 on PostgreSQL 18.3 |
| Phase 02A regression | PASS — 10/10 |
| Phase 02B regression | PASS — 13/13 |
| Phase 02B fresh PostgreSQL/idempotent seed | PASS |

The PostgreSQL workflow suite applies `001 + 002a + 002b`, exercises all three operations and cancellation paths, proves concurrent slot/idempotency behavior, and finishes with `integration.rds_tasks = 0` and `integration.rds_worksites = 0`.
