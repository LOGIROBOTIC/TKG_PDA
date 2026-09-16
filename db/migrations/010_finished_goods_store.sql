-- Finished-goods store. Forward-only addition after 009.
-- No existing table, constraint, customer row or transport behaviour is changed.
--
-- WHAT THIS FILLS
-- ---------------
-- A REMOVE_FULL takes a full container off a machine and the robot leaves it at
-- the layout's DROP position. Completion updates the machine position it
-- emptied and, for the buffer operations, the buffer; the DROP destination gets
-- no record at all. So today nothing knows what is standing in the warehouse,
-- and the warehouse operator has nothing to empty.
--
-- WHY THESE ARE NOT layout_slots EITHER
-- -------------------------------------
-- Same reason as the preparation areas: master.layouts.machine_id is NOT NULL,
-- and the finished-goods store receives output from every machine. The store
-- gets its own identity, and each machine's DROP position is linked to the
-- store location its deliveries physically land in.
--
-- WHAT IS DELIBERATELY NOT DECIDED HERE
-- --------------------------------------
-- How many locations the store has, where they are, and how many containers
-- each holds are customer facts nobody has supplied. No location rows are
-- seeded and capacity is nullable: a null capacity means unknown, and the
-- client shows no fill warning rather than inventing a denominator.
BEGIN;

CREATE TABLE IF NOT EXISTS ops.store_locations (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code            varchar(120) NOT NULL UNIQUE,
    display_name    varchar(200) NOT NULL,
    -- NULL until the customer states how many containers fit here.
    capacity        integer,
    is_enabled      boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_store_location_capacity CHECK (capacity IS NULL OR capacity > 0)
);

-- Which DROP position's completed deliveries land in which store location.
-- Several machines may drop into the same location; one position feeds at most
-- one location, so a delivery is never attributed to two places.
CREATE TABLE IF NOT EXISTS ops.store_location_delivery_slots (
    layout_slot_id      uuid PRIMARY KEY REFERENCES master.layout_slots(id) ON DELETE CASCADE,
    store_location_id   uuid NOT NULL REFERENCES ops.store_locations(id) ON DELETE RESTRICT,
    created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_store_delivery_slots_location
    ON ops.store_location_delivery_slots (store_location_id);

-- One delivered container standing in the store. A location holds as many as
-- were delivered into it: the robot has already physically put them there, so
-- a record is never refused for being inconvenient.
CREATE TABLE IF NOT EXISTS ops.store_units (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    store_location_id       uuid NOT NULL REFERENCES ops.store_locations(id) ON DELETE RESTRICT,
    -- The transport that delivered it. NULL only for a unit recorded by hand.
    transport_request_id    uuid REFERENCES ops.transport_requests(id) ON DELETE RESTRICT,
    part_id                 uuid REFERENCES master.parts(id) ON DELETE RESTRICT,
    container_type_id       uuid REFERENCES master.container_types(id) ON DELETE RESTRICT,
    machine_id              uuid REFERENCES master.machines(id) ON DELETE RESTRICT,
    delivered_at            timestamptz NOT NULL DEFAULT clock_timestamp(),
    -- Set when the warehouse operator states the location was emptied. A
    -- transport reaching a status never removes a unit on its own.
    removed_at              timestamptz,
    removed_by              uuid REFERENCES auth.users(id) ON DELETE RESTRICT,
    removed_device_id       uuid REFERENCES audit.client_devices(id) ON DELETE RESTRICT,
    row_version             bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_store_unit_removed CHECK (
        (removed_at IS NULL AND removed_by IS NULL AND removed_device_id IS NULL)
        OR (removed_at IS NOT NULL AND removed_by IS NOT NULL
            AND removed_device_id IS NOT NULL AND removed_at >= delivered_at)
    )
);

-- One delivery per transport: a completion replayed by reconciliation must not
-- make the store look twice as full as it is.
CREATE UNIQUE INDEX IF NOT EXISTS uq_store_unit_transport
    ON ops.store_units (transport_request_id) WHERE transport_request_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_store_units_standing
    ON ops.store_units (store_location_id, delivered_at) WHERE removed_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_store_units_history
    ON ops.store_units (store_location_id, delivered_at DESC);

-- What was put down and what was taken away, with who and when. Append-only.
CREATE TABLE IF NOT EXISTS ops.store_events (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    store_location_id       uuid NOT NULL REFERENCES ops.store_locations(id) ON DELETE RESTRICT,
    store_unit_id           uuid REFERENCES ops.store_units(id) ON DELETE RESTRICT,
    event_type              varchar(40) NOT NULL,
    part_id                 uuid REFERENCES master.parts(id) ON DELETE RESTRICT,
    container_type_id       uuid REFERENCES master.container_types(id) ON DELETE RESTRICT,
    transport_request_id    uuid REFERENCES ops.transport_requests(id) ON DELETE RESTRICT,
    actor_user_id           uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    device_id               uuid REFERENCES audit.client_devices(id) ON DELETE SET NULL,
    -- Server clock. A tablet's own time is never trusted for ordering.
    occurred_at             timestamptz NOT NULL DEFAULT clock_timestamp(),
    data_json               jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT ck_store_event_type CHECK (event_type IN (
        'UNIT_DELIVERED','UNIT_REMOVED','LOCATION_CLEARED'
    ))
);

CREATE INDEX IF NOT EXISTS ix_store_events_location
    ON ops.store_events (store_location_id, occurred_at DESC);

COMMIT;
