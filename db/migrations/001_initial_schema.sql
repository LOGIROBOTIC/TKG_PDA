-- TKG Otomotiv PDA
-- PostgreSQL initial schema v0.1
-- Phase 1: operator driven; no PLC integration.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS auth;
CREATE SCHEMA IF NOT EXISTS master;
CREATE SCHEMA IF NOT EXISTS ops;
CREATE SCHEMA IF NOT EXISTS integration;
CREATE SCHEMA IF NOT EXISTS audit;

-- ============================================================
-- AUTH
-- ============================================================

CREATE TABLE auth.roles (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code            varchar(50) NOT NULL UNIQUE,
    name            varchar(100) NOT NULL,
    is_system       boolean NOT NULL DEFAULT false,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE auth.users (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    username        varchar(100) NOT NULL,
    password_hash   text NOT NULL,
    full_name       varchar(200) NOT NULL,
    employee_no     varchar(100),
    is_active       boolean NOT NULL DEFAULT true,
    last_login_at   timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_users_username UNIQUE (username)
);

CREATE TABLE auth.user_roles (
    user_id         uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    role_id         uuid NOT NULL REFERENCES auth.roles(id) ON DELETE RESTRICT,
    assigned_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, role_id)
);

-- ============================================================
-- MASTER DATA
-- ============================================================

CREATE TABLE master.machines (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code            varchar(100) NOT NULL UNIQUE,
    display_name    varchar(150) NOT NULL,
    display_order   integer NOT NULL DEFAULT 0,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE auth.user_machine_permissions (
    user_id             uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    machine_id          uuid NOT NULL REFERENCES master.machines(id) ON DELETE CASCADE,
    can_view            boolean NOT NULL DEFAULT true,
    can_operate         boolean NOT NULL DEFAULT false,
    can_change_variant  boolean NOT NULL DEFAULT false,
    can_manage_slots    boolean NOT NULL DEFAULT false,
    PRIMARY KEY (user_id, machine_id)
);

CREATE TABLE master.variants (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    machine_id      uuid NOT NULL REFERENCES master.machines(id) ON DELETE RESTRICT,
    code            varchar(50) NOT NULL,
    name            varchar(150) NOT NULL,
    display_order   integer NOT NULL DEFAULT 0,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_variants_machine_code UNIQUE (machine_id, code),
    CONSTRAINT uq_variants_id_machine UNIQUE (id, machine_id)
);

CREATE TABLE master.parts (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    part_code       varchar(120) NOT NULL UNIQUE,
    description     text,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE master.container_types (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code            varchar(100) NOT NULL UNIQUE,
    name            varchar(150) NOT NULL,
    category        varchar(30) NOT NULL DEFAULT 'CONTAINER',
    length_mm       integer,
    width_mm        integer,
    height_mm       integer,
    capacity_qty    numeric(14,3),
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_container_category CHECK (category IN ('CONTAINER','PALLET','CART','BOX','MIX_PALLET','OTHER')),
    CONSTRAINT ck_container_dimensions CHECK (
        (length_mm IS NULL OR length_mm > 0) AND
        (width_mm IS NULL OR width_mm > 0) AND
        (height_mm IS NULL OR height_mm > 0)
    )
);

-- Main "Parça" card/selection under a variant.
CREATE TABLE master.product_groups (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    variant_id      uuid NOT NULL REFERENCES master.variants(id) ON DELETE CASCADE,
    main_part_id    uuid NOT NULL REFERENCES master.parts(id) ON DELETE RESTRICT,
    name            varchar(200),
    display_order   integer NOT NULL DEFAULT 0,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_product_group_variant_main UNIQUE (variant_id, main_part_id)
);

-- Related / "Alt Parça" rows for a selected main part.
CREATE TABLE master.product_group_parts (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    product_group_id    uuid NOT NULL REFERENCES master.product_groups(id) ON DELETE CASCADE,
    part_id             uuid NOT NULL REFERENCES master.parts(id) ON DELETE RESTRICT,
    container_type_id   uuid REFERENCES master.container_types(id) ON DELETE RESTRICT,
    relation_type       varchar(30) NOT NULL DEFAULT 'RELATED',
    lot_quantity        numeric(14,3),
    display_order       integer NOT NULL DEFAULT 0,
    is_active           boolean NOT NULL DEFAULT true,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_group_part_relation CHECK (relation_type IN ('ALT_PART','RELATED','CONSUMABLE','OTHER')),
    CONSTRAINT uq_product_group_part UNIQUE (product_group_id, part_id)
);

-- Optional planning fields retained from the customer planning workbook.
CREATE TABLE master.part_planning_metrics (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    variant_id              uuid NOT NULL REFERENCES master.variants(id) ON DELETE CASCADE,
    product_group_id        uuid REFERENCES master.product_groups(id) ON DELETE CASCADE,
    part_id                 uuid NOT NULL REFERENCES master.parts(id) ON DELETE RESTRICT,
    container_type_id       uuid REFERENCES master.container_types(id) ON DELETE RESTRICT,
    container_dimensions    varchar(100),
    lot_quantity            numeric(14,3),
    cycle_time_seconds      numeric(14,3),
    annual_volume           numeric(18,3),
    weekly_volume           numeric(18,3),
    daily_volume            numeric(18,3),
    mix_pallet_need         numeric(14,3),
    agv_moves_per_day       numeric(14,3),
    area_need               numeric(18,3),
    source_note             text,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now()
);

-- A part may appear in multiple product groups. Uniqueness is scoped to the
-- variant and treats ungrouped planning rows explicitly because PostgreSQL
-- UNIQUE constraints consider NULL values distinct by default.
CREATE UNIQUE INDEX uq_planning_group_part
    ON master.part_planning_metrics (variant_id, product_group_id, part_id)
    WHERE product_group_id IS NOT NULL;

CREATE UNIQUE INDEX uq_planning_variant_part_without_group
    ON master.part_planning_metrics (variant_id, part_id)
    WHERE product_group_id IS NULL;

-- A navigation profile is machine-scoped and may be assigned to multiple
-- variants. Variant-to-profile ownership is modeled by the assignment table.
CREATE TABLE master.navigation_profiles (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    machine_id          uuid NOT NULL REFERENCES master.machines(id) ON DELETE RESTRICT,
    code                varchar(100) NOT NULL,
    name                varchar(200) NOT NULL,
    version             integer NOT NULL DEFAULT 1,
    status              varchar(20) NOT NULL DEFAULT 'DRAFT',
    base_map_name           varchar(200),
    robot_map_name          varchar(200),
    route_profile_name      varchar(200),
    stitch_profile_name     varchar(200),
    config_json         jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_by          uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    published_at        timestamptz,
    CONSTRAINT ck_navigation_status CHECK (status IN ('DRAFT','PUBLISHED','ARCHIVED')),
    CONSTRAINT uq_navigation_machine_code_version UNIQUE (machine_id, code, version),
    CONSTRAINT uq_navigation_id_machine UNIQUE (id, machine_id)
);

CREATE UNIQUE INDEX uq_navigation_profile_published_version
    ON master.navigation_profiles (machine_id, code)
    WHERE status = 'PUBLISHED';

CREATE TABLE master.variant_navigation_profile_assignments (
    variant_id              uuid NOT NULL,
    machine_id              uuid NOT NULL,
    navigation_profile_id   uuid NOT NULL,
    is_default              boolean NOT NULL DEFAULT false,
    assigned_at             timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (variant_id, navigation_profile_id),
    CONSTRAINT fk_variant_navigation_assignment_variant
        FOREIGN KEY (variant_id, machine_id)
        REFERENCES master.variants (id, machine_id) ON DELETE CASCADE,
    CONSTRAINT fk_variant_navigation_assignment_profile
        FOREIGN KEY (navigation_profile_id, machine_id)
        REFERENCES master.navigation_profiles (id, machine_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX uq_variant_default_navigation_profile
    ON master.variant_navigation_profile_assignments (variant_id)
    WHERE is_default;

CREATE INDEX ix_variant_navigation_profile_profile
    ON master.variant_navigation_profile_assignments (navigation_profile_id);

-- A physical layout is machine-scoped and may be shared by multiple variants.
CREATE TABLE master.layouts (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    machine_id              uuid NOT NULL REFERENCES master.machines(id) ON DELETE RESTRICT,
    code                    varchar(100) NOT NULL,
    name                    varchar(200) NOT NULL,
    version                 integer NOT NULL DEFAULT 1,
    status                  varchar(20) NOT NULL DEFAULT 'DRAFT',
    created_by              uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    created_at              timestamptz NOT NULL DEFAULT now(),
    published_at            timestamptz,
    CONSTRAINT ck_layout_status CHECK (status IN ('DRAFT','PUBLISHED','ARCHIVED')),
    CONSTRAINT uq_layout_machine_code_version UNIQUE (machine_id, code, version),
    CONSTRAINT uq_layout_id_machine UNIQUE (id, machine_id)
);

CREATE UNIQUE INDEX uq_layout_published_version
    ON master.layouts (machine_id, code)
    WHERE status = 'PUBLISHED';

CREATE TABLE master.variant_layout_assignments (
    variant_id      uuid NOT NULL,
    machine_id      uuid NOT NULL,
    layout_id       uuid NOT NULL,
    is_default      boolean NOT NULL DEFAULT false,
    assigned_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (variant_id, layout_id),
    CONSTRAINT fk_variant_layout_assignment_variant
        FOREIGN KEY (variant_id, machine_id)
        REFERENCES master.variants (id, machine_id) ON DELETE CASCADE,
    CONSTRAINT fk_variant_layout_assignment_layout
        FOREIGN KEY (layout_id, machine_id)
        REFERENCES master.layouts (id, machine_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX uq_variant_default_layout
    ON master.variant_layout_assignments (variant_id)
    WHERE is_default;

CREATE INDEX ix_variant_layout_assignment_layout
    ON master.variant_layout_assignments (layout_id);

CREATE TABLE master.layout_slots (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    layout_id                   uuid NOT NULL REFERENCES master.layouts(id) ON DELETE CASCADE,
    code                        varchar(120) NOT NULL,
    display_name                varchar(200) NOT NULL,
    slot_role                   varchar(40) NOT NULL,
    default_container_type_id   uuid REFERENCES master.container_types(id) ON DELETE RESTRICT,
    ui_x                        numeric(12,4),
    ui_y                        numeric(12,4),
    ui_width                    numeric(12,4),
    ui_height                   numeric(12,4),
    display_order               integer NOT NULL DEFAULT 0,
    allows_empty_load           boolean NOT NULL DEFAULT true,
    allows_full_load            boolean NOT NULL DEFAULT true,
    is_enabled                  boolean NOT NULL DEFAULT true,
    metadata_json               jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_layout_slot_role CHECK (slot_role IN (
        'MACHINE','STAGING','EMPTY_STORAGE','FULL_STORAGE','WAREHOUSE','PICKUP','DROP','SHARED','OTHER'
    )),
    CONSTRAINT uq_layout_slot_code UNIQUE (layout_id, code)
);

CREATE TABLE master.slot_groups (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    layout_id       uuid NOT NULL REFERENCES master.layouts(id) ON DELETE CASCADE,
    code            varchar(100) NOT NULL,
    name            varchar(200) NOT NULL,
    purpose         varchar(40) NOT NULL,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_slot_group_purpose CHECK (purpose IN ('SOURCE','DESTINATION','STAGING','WAREHOUSE','OTHER')),
    CONSTRAINT uq_slot_group_code UNIQUE (layout_id, code)
);

CREATE TABLE master.slot_group_members (
    slot_group_id   uuid NOT NULL REFERENCES master.slot_groups(id) ON DELETE CASCADE,
    layout_slot_id  uuid NOT NULL REFERENCES master.layout_slots(id) ON DELETE CASCADE,
    priority        integer NOT NULL DEFAULT 0,
    PRIMARY KEY (slot_group_id, layout_slot_id)
);

CREATE TABLE master.task_types (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code            varchar(50) NOT NULL UNIQUE,
    name            varchar(150) NOT NULL,
    is_active       boolean NOT NULL DEFAULT true
);

CREATE TABLE master.task_templates (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    variant_id                  uuid NOT NULL REFERENCES master.variants(id) ON DELETE CASCADE,
    product_group_id            uuid REFERENCES master.product_groups(id) ON DELETE CASCADE,
    task_type_id                uuid NOT NULL REFERENCES master.task_types(id) ON DELETE RESTRICT,
    source_slot_group_id        uuid REFERENCES master.slot_groups(id) ON DELETE RESTRICT,
    destination_slot_group_id   uuid REFERENCES master.slot_groups(id) ON DELETE RESTRICT,
    rds_task_label              varchar(200),
    priority                    integer NOT NULL DEFAULT 100,
    config_json                 jsonb NOT NULL DEFAULT '{}'::jsonb,
    is_active                   boolean NOT NULL DEFAULT true,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now()
);

-- ============================================================
-- RUNTIME / OPERATIONS
-- ============================================================

CREATE TABLE ops.machine_runtime_state (
    machine_id              uuid PRIMARY KEY REFERENCES master.machines(id) ON DELETE CASCADE,
    active_variant_id       uuid REFERENCES master.variants(id) ON DELETE RESTRICT,
    active_layout_id        uuid REFERENCES master.layouts(id) ON DELETE RESTRICT,
    active_navigation_id    uuid REFERENCES master.navigation_profiles(id) ON DELETE RESTRICT,
    selected_by             uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    selected_at             timestamptz,
    row_version             bigint NOT NULL DEFAULT 0,
    updated_at              timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE ops.slot_runtime_state (
    layout_slot_id              uuid PRIMARY KEY REFERENCES master.layout_slots(id) ON DELETE CASCADE,
    occupancy_state             varchar(20) NOT NULL DEFAULT 'UNKNOWN',
    load_state                  varchar(20) NOT NULL DEFAULT 'UNKNOWN',
    current_container_type_id   uuid REFERENCES master.container_types(id) ON DELETE RESTRICT,
    current_part_id             uuid REFERENCES master.parts(id) ON DELETE RESTRICT,
    updated_by                  uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    row_version                 bigint NOT NULL DEFAULT 0,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_slot_occupancy CHECK (occupancy_state IN ('VACANT','OCCUPIED','UNKNOWN')),
    CONSTRAINT ck_slot_load CHECK (load_state IN ('EMPTY','FULL','UNKNOWN')),
    CONSTRAINT ck_slot_load_requires_occupancy CHECK (
        occupancy_state = 'OCCUPIED' OR load_state = 'UNKNOWN'
    )
);

CREATE TABLE ops.transport_requests (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    request_no          bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    idempotency_key     varchar(200),
    request_origin      varchar(30) NOT NULL,
    requested_by        uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    machine_id          uuid NOT NULL REFERENCES master.machines(id) ON DELETE RESTRICT,
    variant_id          uuid NOT NULL REFERENCES master.variants(id) ON DELETE RESTRICT,
    layout_id           uuid NOT NULL REFERENCES master.layouts(id) ON DELETE RESTRICT,
    navigation_id       uuid REFERENCES master.navigation_profiles(id) ON DELETE RESTRICT,
    product_group_id    uuid REFERENCES master.product_groups(id) ON DELETE RESTRICT,
    part_id             uuid REFERENCES master.parts(id) ON DELETE RESTRICT,
    container_type_id   uuid REFERENCES master.container_types(id) ON DELETE RESTRICT,
    task_type_id        uuid NOT NULL REFERENCES master.task_types(id) ON DELETE RESTRICT,
    source_slot_id      uuid REFERENCES master.layout_slots(id) ON DELETE RESTRICT,
    destination_slot_id uuid REFERENCES master.layout_slots(id) ON DELETE RESTRICT,
    status              varchar(30) NOT NULL DEFAULT 'CREATED',
    priority            integer NOT NULL DEFAULT 100,
    failure_code        varchar(100),
    failure_message     text,
    correlation_id      uuid NOT NULL DEFAULT gen_random_uuid(),
    created_at          timestamptz NOT NULL DEFAULT now(),
    queued_at           timestamptz,
    started_at          timestamptz,
    completed_at        timestamptz,
    cancelled_at        timestamptz,
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_request_origin CHECK (request_origin IN ('LINE_OPERATOR','WAREHOUSE_OPERATOR','ADMIN','PLC','API')),
    CONSTRAINT ck_transport_status CHECK (status IN (
        'CREATED','VALIDATING','QUEUED','SENT_TO_RDS','RUNNING','COMPLETED','FAILED','CANCELLED'
    ))
);

CREATE UNIQUE INDEX uq_transport_idempotency
    ON ops.transport_requests (idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX ix_transport_machine_status
    ON ops.transport_requests (machine_id, status, created_at DESC);

CREATE INDEX ix_transport_user_created
    ON ops.transport_requests (requested_by, created_at DESC);

CREATE TABLE ops.transport_request_events (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    transport_request_id uuid NOT NULL REFERENCES ops.transport_requests(id) ON DELETE CASCADE,
    actor_user_id       uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    event_type          varchar(100) NOT NULL,
    old_status          varchar(30),
    new_status          varchar(30),
    message             text,
    data_json           jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_transport_events_request_time
    ON ops.transport_request_events (transport_request_id, created_at);

-- ============================================================
-- INTEGRATION / RDS
-- ============================================================

CREATE TABLE integration.rds_worksites (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    navigation_profile_id   uuid REFERENCES master.navigation_profiles(id) ON DELETE CASCADE,
    rds_site_id             varchar(200) NOT NULL,
    area                    varchar(200),
    group_name              varchar(200),
    rds_type                integer,
    last_filled             integer,
    last_locked             integer,
    last_disabled           integer,
    last_content            text,
    last_agv_id             varchar(200),
    last_sync_failed        integer,
    last_sync_at            timestamptz,
    raw_state_json          jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT uq_rds_worksite_profile_site UNIQUE (navigation_profile_id, rds_site_id)
);

CREATE TABLE integration.layout_slot_rds_mapping (
    layout_slot_id      uuid PRIMARY KEY REFERENCES master.layout_slots(id) ON DELETE CASCADE,
    rds_worksite_id     uuid NOT NULL REFERENCES integration.rds_worksites(id) ON DELETE CASCADE,
    created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE integration.rds_tasks (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    transport_request_id    uuid NOT NULL UNIQUE REFERENCES ops.transport_requests(id) ON DELETE CASCADE,
    rds_task_record_id      varchar(200) UNIQUE,
    rds_task_label          varchar(200),
    out_order_no            varchar(200),
    agv_id                  varchar(200),
    rds_status              integer,
    ended_reason            text,
    request_payload_json    jsonb NOT NULL DEFAULT '{}'::jsonb,
    response_payload_json   jsonb NOT NULL DEFAULT '{}'::jsonb,
    last_status_json        jsonb NOT NULL DEFAULT '{}'::jsonb,
    sent_at                 timestamptz,
    started_at              timestamptz,
    ended_at                timestamptz,
    last_sync_at            timestamptz,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_rds_tasks_status
    ON integration.rds_tasks (rds_status, last_sync_at);

-- ============================================================
-- AUDIT / DEVICES
-- ============================================================

CREATE TABLE audit.client_devices (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    device_code     varchar(120) NOT NULL UNIQUE,
    name            varchar(200) NOT NULL,
    device_type     varchar(40) NOT NULL DEFAULT 'PDA',
    is_active       boolean NOT NULL DEFAULT true,
    metadata_json   jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_device_type CHECK (device_type IN ('PDA','ADMIN_WEB','SERVICE','OTHER'))
);

CREATE TABLE audit.audit_logs (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_user_id   uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    device_id       uuid REFERENCES audit.client_devices(id) ON DELETE SET NULL,
    action          varchar(120) NOT NULL,
    entity_type     varchar(120) NOT NULL,
    entity_id       varchar(200),
    before_json     jsonb,
    after_json      jsonb,
    ip_address      inet,
    user_agent      text,
    correlation_id  uuid,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_audit_actor_time
    ON audit.audit_logs (actor_user_id, created_at DESC);

CREATE INDEX ix_audit_entity_time
    ON audit.audit_logs (entity_type, entity_id, created_at DESC);

-- ============================================================
-- BASELINE SEEDS
-- ============================================================

INSERT INTO auth.roles (code, name, is_system) VALUES
    ('ADMIN', 'Admin', true),
    ('SUPERVISOR', 'Supervisor', true),
    ('LINE_OPERATOR', 'Line Operator', true),
    ('WAREHOUSE_OPERATOR', 'Warehouse Operator', true)
ON CONFLICT (code) DO NOTHING;

INSERT INTO master.task_types (code, name) VALUES
    ('BRING_EMPTY', 'Boş Palet/Kasa Getir'),
    ('REMOVE_FULL', 'Dolu Palet/Kasa Götür'),
    ('REMOVE_EMPTY', 'Boş Palet/Kasa Götür')
ON CONFLICT (code) DO NOTHING;

INSERT INTO master.machines (code, display_name, display_order) VALUES
    ('SH-01-A', 'SH-01-A', 1),
    ('SH-01-B', 'SH-01-B', 2),
    ('SH-01-P1--SH-01-P2', 'SH-01-P1--SH-01-P2', 3),
    ('SH-02-P1 -SH-02-P2', 'SH-02-P1 -SH-02-P2', 4),
    ('SEALER', 'SEALER', 5),
    ('PAH-01-A', 'PAH-01-A', 6),
    ('412B', '412B', 7),
    ('PAH-02-A', 'PAH-02-A', 8),
    ('GAR', 'GAR', 9)
ON CONFLICT (code) DO NOTHING;

-- Variant counts observed in customer planning/layout workbooks.
WITH seed(machine_code, variant_code, variant_name, display_order) AS (
    VALUES
      ('SH-01-A','1','1. VARYANT',1),('SH-01-A','2','2. VARYANT',2),('SH-01-A','3','3. VARYANT',3),('SH-01-A','4','4. VARYANT',4),('SH-01-A','5','5. VARYANT',5),
      ('SH-01-B','1','1. VARYANT',1),('SH-01-B','2','2. VARYANT',2),('SH-01-B','3','3. VARYANT',3),
      ('SH-01-P1--SH-01-P2','1','1. VARYANT',1),('SH-01-P1--SH-01-P2','2','2. VARYANT',2),
      ('SH-02-P1 -SH-02-P2','1','1. VARYANT',1),('SH-02-P1 -SH-02-P2','2','2. VARYANT',2),
      ('SEALER','1','1. VARYANT',1),('SEALER','2','2. VARYANT',2),('SEALER','3','3. VARYANT',3),
      ('PAH-01-A','1','1. VARYANT',1),('PAH-01-A','2','2. VARYANT',2),
      ('412B','1','1. VARYANT',1),('412B','2','2. VARYANT',2),('412B','3','3. VARYANT',3),
      ('PAH-02-A','1','1. VARYANT',1),('PAH-02-A','2','2. VARYANT',2),
      ('GAR','1','1. VARYANT',1),('GAR','2','2. VARYANT',2),('GAR','3','3. VARYANT',3)
)
INSERT INTO master.variants (machine_id, code, name, display_order)
SELECT m.id, s.variant_code, s.variant_name, s.display_order
FROM seed s
JOIN master.machines m ON m.code = s.machine_code
ON CONFLICT (machine_id, code) DO NOTHING;

COMMIT;
