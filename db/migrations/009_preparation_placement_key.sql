-- Idempotency for pallet placement. Forward-only addition after 008.
--
-- A tablet that loses the response to "doldur" retries. Without a key the
-- retry either creates a second pallet or is rejected by the one-pallet-per-area
-- index as though someone else had taken the area, which reads to the operator
-- as a conflict that is not real. The key makes the retry return the pallet the
-- first call created.
--
-- Scoped per area rather than globally: two areas legitimately being filled in
-- the same screen session must not collide, and a replay is only a replay of
-- the placement it was issued for.
BEGIN;

ALTER TABLE ops.preparation_pallets
    ADD COLUMN IF NOT EXISTS placement_key varchar(200);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = 'ops.preparation_pallets'::regclass
                     AND conname = 'ck_preparation_placement_key') THEN
        ALTER TABLE ops.preparation_pallets ADD CONSTRAINT ck_preparation_placement_key
            CHECK (placement_key IS NULL OR length(btrim(placement_key)) > 0);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_preparation_placement_key
    ON ops.preparation_pallets (preparation_area_id, placement_key)
    WHERE placement_key IS NOT NULL;

COMMIT;
