-- ============================================================
-- 002_operator_employee_identity.sql
-- WP-01A — Operator employee / registry (sicil) identity contract
-- ============================================================
-- Forward migration. Apply after 001_initial_schema.sql.
--
-- Purpose:
--   Enforce normalized uniqueness for the existing auth.users.employee_no
--   so it can serve as the Operator login identity (sicil numarası).
--
-- Rules (see docs/CUSTOMER_WP01A_OPERATOR_EMPLOYEE_IDENTITY.md):
--   * employee_no stays a STRING; leading zeros are significant.
--   * Uniqueness is trimmed + case-insensitive: UPPER(BTRIM(employee_no)).
--   * NULL and blank/whitespace-only values are exempt (multiple allowed);
--     employee_no is intentionally NOT globally NOT NULL, because
--     admin/service identities may exist without an employee number.
--   * "001245" and "1245" remain DISTINCT (no numeric parsing).
--   * "ABC123", "abc123", " ABC123 ", "abc123 " all CONFLICT.
--
-- Safety:
--   Creating the UNIQUE index fails if normalized duplicates already exist.
--   The migration deliberately does not rewrite or de-duplicate existing
--   customer/production data — a conflict must be resolved by a human.
--
-- Idempotency:
--   IF NOT EXISTS makes re-application a no-op once the index is present, so
--   the migration can be safely applied to an existing pre-WP01A database
--   without dropping or recreating data. The first (creating) application
--   still fails on pre-existing normalized duplicates, by design.

BEGIN;

CREATE UNIQUE INDEX IF NOT EXISTS uq_users_employee_no_normalized
    ON auth.users (UPPER(BTRIM(employee_no)))
    WHERE employee_no IS NOT NULL AND BTRIM(employee_no) <> '';

COMMIT;
