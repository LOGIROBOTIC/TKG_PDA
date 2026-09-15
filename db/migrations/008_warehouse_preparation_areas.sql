-- Shared warehouse preparation areas. Forward-only addition after 007.
-- No existing table, constraint, customer row or physical mapping is rewritten.
--
-- WHY THESE ARE NOT layout_slots
-- ------------------------------
-- master.layouts.machine_id is NOT NULL and master.layout_slots belongs to a
-- layout, so every existing position is owned by exactly one machine. The four
-- preparation areas are shared by all nine machines: duplicating them per
-- machine would let two machines claim the same physical pallet, and recording
-- them under one machine would be a lie the transport rules then enforce.
-- Relaxing machine_id would loosen a constraint every existing query relies on.
-- They therefore get their own identity here, and reach RDS through their own
-- mapping rather than integration.layout_slot_rds_mapping.
--
-- CAPACITY IS A CUSTOMER DECISION
-- -------------------------------
-- Four areas, at most one pallet each, confirmed 15 September 2026. The area
-- number is a logical label for the operator, never an RDS point name: the
-- physical point comes from the mapping table, per navigation profile, because
-- the same floor position has a different point name in each map.
BEGIN;

-- 1. The areas themselves. Rows are seeded here because the count is a settled
--    customer decision, but nothing about their physical location is invented.
CREATE TABLE IF NOT EXISTS ops.preparation_areas (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    area_number     integer NOT NULL UNIQUE,
    display_name    varchar(200) NOT NULL,
    is_enabled      boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_preparation_area_number CHECK (area_number BETWEEN 1 AND 4)
);

INSERT INTO ops.preparation_areas (area_number, display_name)
VALUES (1, 'Hazırlık Alanı 1'),
       (2, 'Hazırlık Alanı 2'),
       (3, 'Hazırlık Alanı 3'),
       (4, 'Hazırlık Alanı 4')
ON CONFLICT (area_number) DO NOTHING;

-- 2. Physical identity, one point per navigation profile. The worksite already
--    carries its profile, so the unique constraint below is what stops two
--    profiles' points being attached to the same area twice over.
CREATE TABLE IF NOT EXISTS integration.preparation_area_rds_mapping (
    preparation_area_id uuid NOT NULL REFERENCES ops.preparation_areas(id) ON DELETE CASCADE,
    rds_worksite_id     uuid NOT NULL REFERENCES integration.rds_worksites(id) ON DELETE CASCADE,
    created_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (preparation_area_id, rds_worksite_id),
    CONSTRAINT uq_preparation_area_worksite UNIQUE (rds_worksite_id)
);

-- 3. What physically stands in an area right now, and what stood there before.
--    released_at is what closes a row: a pallet is only gone once its physical
--    collection is verified, never because a request reached a status.
CREATE TABLE IF NOT EXISTS ops.preparation_pallets (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    preparation_area_id     uuid NOT NULL REFERENCES ops.preparation_areas(id) ON DELETE RESTRICT,
    container_type_id       uuid NOT NULL REFERENCES master.container_types(id) ON DELETE RESTRICT,
    load_state              varchar(20) NOT NULL,
    -- NULL for an empty container: the pallet holds no material. This is not
    -- the part of the request it may later serve.
    part_id                 uuid REFERENCES master.parts(id) ON DELETE RESTRICT,
    -- NULL while an empty container is staged ahead of demand. Filled material
    -- must name its request from the moment it is placed.
    transport_request_id    uuid REFERENCES ops.transport_requests(id) ON DELETE RESTRICT,
    placed_by               uuid NOT NULL REFERENCES auth.users(id) ON DELETE RESTRICT,
    placed_device_id        uuid NOT NULL REFERENCES audit.client_devices(id) ON DELETE RESTRICT,
    placed_at               timestamptz NOT NULL DEFAULT clock_timestamp(),
    ready_at                timestamptz,
    ready_by                uuid REFERENCES auth.users(id) ON DELETE RESTRICT,
    released_at             timestamptz,
    released_by             uuid REFERENCES auth.users(id) ON DELETE RESTRICT,
    released_reason         varchar(60),
    row_version             bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_preparation_pallet_load CHECK (load_state IN ('EMPTY','FULL')),
    -- Filled material is prepared only against a real request, and always names
    -- its part. An empty container carries neither.
    CONSTRAINT ck_preparation_pallet_content CHECK (
        (load_state = 'FULL' AND part_id IS NOT NULL AND transport_request_id IS NOT NULL)
        OR (load_state = 'EMPTY' AND part_id IS NULL)
    ),
    CONSTRAINT ck_preparation_pallet_ready CHECK (
        (ready_at IS NULL AND ready_by IS NULL)
        OR (ready_at IS NOT NULL AND ready_by IS NOT NULL AND ready_at >= placed_at)
    ),
    CONSTRAINT ck_preparation_pallet_released CHECK (
        (released_at IS NULL AND released_by IS NULL AND released_reason IS NULL)
        OR (released_at IS NOT NULL AND released_by IS NOT NULL
            AND released_reason IS NOT NULL AND released_at >= placed_at)
    ),
    -- A pallet cannot be handed to a transport before it is marked ready.
    CONSTRAINT ck_preparation_pallet_collect_after_ready CHECK (
        released_reason IS DISTINCT FROM 'COLLECTED' OR ready_at IS NOT NULL
    )
);

-- The single-pallet-per-area rule, held by the database rather than by whoever
-- happens to check first. Two tablets racing to fill area 2 cannot both win.
CREATE UNIQUE INDEX IF NOT EXISTS uq_preparation_area_active_pallet
    ON ops.preparation_pallets (preparation_area_id) WHERE released_at IS NULL;

-- One live pallet per request: a second area cannot also claim to be preparing
-- the same demand.
CREATE UNIQUE INDEX IF NOT EXISTS uq_preparation_request_active_pallet
    ON ops.preparation_pallets (transport_request_id)
    WHERE released_at IS NULL AND transport_request_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_preparation_pallet_area_history
    ON ops.preparation_pallets (preparation_area_id, placed_at DESC);

-- 4. Movement history: what was put down, what was picked up, by whom, when.
--    Append-only; the pallet row carries current state, this carries the trail.
CREATE TABLE IF NOT EXISTS ops.preparation_events (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    preparation_area_id     uuid NOT NULL REFERENCES ops.preparation_areas(id) ON DELETE RESTRICT,
    preparation_pallet_id   uuid REFERENCES ops.preparation_pallets(id) ON DELETE RESTRICT,
    event_type              varchar(40) NOT NULL,
    part_id                 uuid REFERENCES master.parts(id) ON DELETE RESTRICT,
    container_type_id       uuid REFERENCES master.container_types(id) ON DELETE RESTRICT,
    transport_request_id    uuid REFERENCES ops.transport_requests(id) ON DELETE RESTRICT,
    actor_user_id           uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    device_id               uuid REFERENCES audit.client_devices(id) ON DELETE SET NULL,
    -- Server clock. A tablet's own time is never trusted for ordering.
    occurred_at             timestamptz NOT NULL DEFAULT clock_timestamp(),
    data_json               jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT ck_preparation_event_type CHECK (event_type IN (
        'PALLET_PLACED','PALLET_MARKED_READY','PALLET_COLLECTED','PALLET_RELEASED','AREA_CLEARED'
    ))
);

CREATE INDEX IF NOT EXISTS ix_preparation_events_area
    ON ops.preparation_events (preparation_area_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_preparation_events_request
    ON ops.preparation_events (transport_request_id) WHERE transport_request_id IS NOT NULL;

COMMIT;
