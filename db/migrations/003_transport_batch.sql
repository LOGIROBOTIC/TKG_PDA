-- ============================================================
-- 003_transport_batch.sql
-- WP-04 — Transport batch business grouping boundary
-- ============================================================
-- Forward migration. Apply after 001_initial_schema.sql and
-- 002_operator_employee_identity.sql.
--
-- Purpose:
--   Support the customer's future multi-part selection without turning one
--   TransportRequest into a multi-part RDS task. A batch is ONLY a business
--   grouping boundary: it does not get its own RDS task, its own execution
--   status state machine, or an occupancy state of its own.
--
-- Architecture (fixed):
--   TransportBatch -> TransportRequest -> TransportRequest -> TransportRequest
--
--   Each child ops.transport_requests row keeps its own UUID, idempotency
--   key, operation, part, source/destination, priority, execution status,
--   audit/events and (future) RDS taskRecordId — completely unchanged from
--   the existing single-request contract.
--
-- Batch state:
--   Deliberately NOT modeled here. Batch state is derived from its child
--   requests' existing statuses when needed; no second status/state-machine
--   column is introduced on ops.transport_batches.
--
-- Backward compatibility:
--   ops.transport_requests.batch_id is nullable. Every existing/standalone
--   TransportRequest remains valid with batch_id NULL.
--
-- Idempotency:
--   IF NOT EXISTS throughout makes re-application a no-op, so this
--   migration can be safely applied to an existing database in place
--   (matching the 002 convention), including local dev bootstraps that
--   apply every migration unconditionally on every startup.

BEGIN;

CREATE TABLE IF NOT EXISTS ops.transport_batches (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    idempotency_key     varchar(200),
    request_origin      varchar(30) NOT NULL,
    requested_by        uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    machine_id          uuid NOT NULL REFERENCES master.machines(id) ON DELETE RESTRICT,
    created_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_transport_batch_origin CHECK (request_origin IN ('LINE_OPERATOR','WAREHOUSE_OPERATOR','ADMIN','PLC','API'))
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_transport_batch_idempotency
    ON ops.transport_batches (idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_transport_batch_machine_created
    ON ops.transport_batches (machine_id, created_at DESC);

ALTER TABLE ops.transport_requests
    ADD COLUMN IF NOT EXISTS batch_id uuid REFERENCES ops.transport_batches(id) ON DELETE RESTRICT;

CREATE INDEX IF NOT EXISTS ix_transport_requests_batch
    ON ops.transport_requests (batch_id)
    WHERE batch_id IS NOT NULL;

COMMIT;
