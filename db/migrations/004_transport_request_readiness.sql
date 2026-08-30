-- ============================================================
-- 004_transport_request_readiness.sql
-- WP-05A — Warehouse preparation readiness (business), separate from
-- execution status
-- ============================================================
-- Forward migration. Apply after 001/002/003.
--
-- Purpose:
--   Add BUSINESS readiness to ops.transport_requests without touching the
--   existing execution state machine (status stays exactly CREATED,
--   VALIDATING, QUEUED, SENT_TO_RDS, RUNNING, COMPLETED, FAILED, CANCELLED).
--   Readiness answers "is the material ready to move"; status answers
--   "where is this request in execution". They are independent.
--
-- Rules:
--   NOT_REQUIRED         -> already execution-eligible today (default;
--                            preserves every existing row/workflow exactly).
--   WAITING_PREPARATION  -> not dispatchable until explicitly marked READY.
--   READY                -> dispatchable; ready_at records when this
--                            happened, set once (never reset by replay).
--   No regression WAITING_PREPARATION -> READY -> WAITING_PREPARATION is
--   modeled; no batch-level readiness/status is introduced (TransportBatch
--   remains a pure grouping boundary — each child keeps its own readiness).
--
-- Backward compatibility:
--   readiness_state defaults to 'NOT_REQUIRED' so every existing row
--   remains valid and behaves exactly as it does today. ready_at is
--   nullable and NULL for every pre-existing row.
--
-- Idempotency:
--   IF NOT EXISTS / ADD COLUMN IF NOT EXISTS throughout, matching the 002/003
--   convention, so this is safe to re-apply to an existing database.

BEGIN;

ALTER TABLE ops.transport_requests
    ADD COLUMN IF NOT EXISTS readiness_state varchar(30) NOT NULL DEFAULT 'NOT_REQUIRED';

ALTER TABLE ops.transport_requests
    ADD COLUMN IF NOT EXISTS ready_at timestamptz;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_transport_readiness'
    ) THEN
        ALTER TABLE ops.transport_requests
            ADD CONSTRAINT ck_transport_readiness
            CHECK (readiness_state IN ('NOT_REQUIRED', 'WAITING_PREPARATION', 'READY'));
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_transport_requests_readiness
    ON ops.transport_requests (readiness_state)
    WHERE readiness_state = 'WAITING_PREPARATION';

COMMIT;
