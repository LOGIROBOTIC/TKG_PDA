-- ============================================================
-- 005_layout_slot_part_mapping.sql
-- Explicit, curated Part <-> Layout Slot mapping for automatic
-- machine-side slot resolution
-- ============================================================
-- Forward migration. Apply after 001/002/003/004.
--
-- Purpose:
--   Give the Operator UX a reliable way to resolve "which physical machine
--   slot does this part belong to" WITHOUT the Operator manually tapping a
--   slot code. This is a first-class relational table, not a re-use of the
--   existing master.layout_slots.metadata_json heuristic — that JSON field
--   (canonical_part_codes / part_association_unresolved / confidence) is
--   automated Excel-extraction evidence with self-reported gaps and is used
--   only for PART_CONFLICT exclusion, never as a positive dispatch index.
--
-- Cardinality:
--   Many-to-many. A physical slot may legitimately hold several customer
--   part codes (main + alternates); a part may also appear in more than one
--   physical slot, and in different slots across different variant-specific
--   layouts. No "one part -> one global slot" constraint is enforced.
--
-- Scope:
--   This table records ONLY the identity relationship (which part belongs
--   on which slot). It intentionally carries no execution/runtime state —
--   occupancy and load remain exclusively in ops.slot_runtime_state.
--
-- Idempotency:
--   IF NOT EXISTS throughout, matching the 002/003/004 convention.

BEGIN;

CREATE TABLE IF NOT EXISTS master.layout_slot_parts (
    layout_slot_id  uuid NOT NULL REFERENCES master.layout_slots(id) ON DELETE CASCADE,
    part_id         uuid NOT NULL REFERENCES master.parts(id) ON DELETE CASCADE,
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (layout_slot_id, part_id)
);

-- Reverse lookup: "which slots is this part eligible for" (the direction
-- automatic machine-slot resolution actually queries).
CREATE INDEX IF NOT EXISTS ix_layout_slot_parts_part_id
    ON master.layout_slot_parts (part_id);

COMMIT;
