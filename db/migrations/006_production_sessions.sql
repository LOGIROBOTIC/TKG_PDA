-- Production-session foundation. Forward-only addition after 005.
-- No customer data, task type, physical mapping or existing state is rewritten.
BEGIN;

-- Preserve the selected customer main-part/variant identity in session history.
CREATE UNIQUE INDEX IF NOT EXISTS uq_product_groups_production_identity
    ON master.product_groups (id, variant_id, main_part_id);

CREATE TABLE IF NOT EXISTS ops.production_sessions (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    machine_id          uuid NOT NULL REFERENCES ops.machine_runtime_state(machine_id) ON DELETE RESTRICT,
    variant_id          uuid NOT NULL,
    product_group_id    uuid NOT NULL,
    main_part_id        uuid NOT NULL,
    start_key           varchar(200) NOT NULL CHECK (length(btrim(start_key)) > 0),
    started_by          uuid NOT NULL REFERENCES auth.users(id) ON DELETE RESTRICT,
    started_device_id   uuid NOT NULL REFERENCES audit.client_devices(id) ON DELETE RESTRICT,
    started_at          timestamptz NOT NULL DEFAULT clock_timestamp(),
    ended_by            uuid REFERENCES auth.users(id) ON DELETE RESTRICT,
    ended_device_id     uuid REFERENCES audit.client_devices(id) ON DELETE RESTRICT,
    ended_at            timestamptz,
    CONSTRAINT fk_production_machine_variant FOREIGN KEY (variant_id, machine_id)
        REFERENCES master.variants(id, machine_id) ON DELETE RESTRICT,
    CONSTRAINT fk_production_product_identity FOREIGN KEY (product_group_id, variant_id, main_part_id)
        REFERENCES master.product_groups(id, variant_id, main_part_id) ON DELETE RESTRICT,
    CONSTRAINT uq_production_start_key UNIQUE (machine_id, start_key),
    CONSTRAINT uq_production_machine_identity UNIQUE (id, machine_id),
    CONSTRAINT ck_production_end CHECK (
        (ended_at IS NULL AND ended_by IS NULL AND ended_device_id IS NULL)
        OR (ended_at IS NOT NULL AND ended_by IS NOT NULL AND ended_device_id IS NOT NULL
            AND ended_at >= started_at)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_production_active_machine
    ON ops.production_sessions (machine_id) WHERE ended_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_production_machine_history
    ON ops.production_sessions (machine_id, started_at DESC);

-- Old transports remain valid and unassociated; new ones are linked by the
-- workflow boundary while holding the same machine lock as start/end.
ALTER TABLE ops.transport_requests ADD COLUMN IF NOT EXISTS production_session_id uuid;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = 'ops.transport_requests'::regclass
                     AND conname = 'fk_transport_production_machine') THEN
        ALTER TABLE ops.transport_requests ADD CONSTRAINT fk_transport_production_machine
            FOREIGN KEY (production_session_id, machine_id)
            REFERENCES ops.production_sessions(id, machine_id) ON DELETE RESTRICT;
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_transport_production_session
    ON ops.transport_requests (production_session_id) WHERE production_session_id IS NOT NULL;

COMMIT;
