-- TKG Otomotiv PDA
-- Phase 01 database smoke test.
-- Run after db/migrations/001_initial_schema.sql against a disposable database.
-- Every mutation is rolled back.

BEGIN;

-- All required application schemas exist.
DO $test$
DECLARE
    schema_count integer;
BEGIN
    SELECT count(*)
      INTO schema_count
      FROM pg_namespace
     WHERE nspname IN ('auth', 'master', 'ops', 'integration', 'audit');

    IF schema_count <> 5 THEN
        RAISE EXCEPTION 'Expected five application schemas, found %', schema_count;
    END IF;
END
$test$;

-- The baseline contains exactly the nine customer-facing machine labels.
DO $test$
DECLARE
    machine_count integer;
BEGIN
    SELECT count(*) INTO machine_count FROM master.machines;

    IF machine_count <> 9 THEN
        RAISE EXCEPTION 'Expected exactly nine baseline machines, found %', machine_count;
    END IF;

    IF EXISTS (
        WITH expected(code) AS (
            VALUES
                ('SH-01-A'),
                ('SH-01-B'),
                ('SH-01-P1--SH-01-P2'),
                ('SH-02-P1 -SH-02-P2'),
                ('SEALER'),
                ('PAH-01-A'),
                ('412B'),
                ('PAH-02-A'),
                ('GAR')
        )
        SELECT code FROM expected
        EXCEPT
        SELECT code FROM master.machines
    ) OR EXISTS (
        WITH expected(code) AS (
            VALUES
                ('SH-01-A'),
                ('SH-01-B'),
                ('SH-01-P1--SH-01-P2'),
                ('SH-02-P1 -SH-02-P2'),
                ('SEALER'),
                ('PAH-01-A'),
                ('412B'),
                ('PAH-02-A'),
                ('GAR')
        )
        SELECT code FROM master.machines
        EXCEPT
        SELECT code FROM expected
    ) THEN
        RAISE EXCEPTION 'Baseline machine labels do not match the required customer labels';
    END IF;
END
$test$;

-- SH-01-A has the five expected seeded variants.
DO $test$
DECLARE
    variant_count integer;
BEGIN
    SELECT count(*)
      INTO variant_count
      FROM master.variants v
      JOIN master.machines m ON m.id = v.machine_id
     WHERE m.code = 'SH-01-A';

    IF variant_count <> 5 THEN
        RAISE EXCEPTION 'Expected five SH-01-A variants, found %', variant_count;
    END IF;

    IF EXISTS (
        SELECT v.code
          FROM master.variants AS v
          JOIN master.machines AS m ON m.id = v.machine_id
         WHERE m.code = 'SH-01-A'
        EXCEPT
        SELECT code FROM (VALUES ('1'), ('2'), ('3'), ('4'), ('5')) AS expected(code)
    ) OR EXISTS (
        SELECT code FROM (VALUES ('1'), ('2'), ('3'), ('4'), ('5')) AS expected(code)
        EXCEPT
        SELECT v.code
          FROM master.variants AS v
          JOIN master.machines AS m ON m.id = v.machine_id
         WHERE m.code = 'SH-01-A'
    ) THEN
        RAISE EXCEPTION 'SH-01-A variant codes do not match the expected set';
    END IF;
END
$test$;

-- The four roles and three task types exist exactly.
DO $test$
BEGIN
    IF (SELECT count(*) FROM auth.roles) <> 4
       OR EXISTS (
            SELECT code FROM auth.roles
            EXCEPT
            SELECT code FROM (VALUES
                ('ADMIN'), ('SUPERVISOR'), ('LINE_OPERATOR'), ('WAREHOUSE_OPERATOR')
            ) AS expected(code)
       ) THEN
        RAISE EXCEPTION 'Baseline roles do not match the required role set';
    END IF;

    IF (SELECT count(*) FROM master.task_types) <> 3
       OR EXISTS (
            SELECT code FROM master.task_types
            EXCEPT
            SELECT code FROM (VALUES
                ('BRING_EMPTY'), ('REMOVE_FULL'), ('REMOVE_EMPTY')
            ) AS expected(code)
       ) THEN
        RAISE EXCEPTION 'Baseline task types do not match the required task-type set';
    END IF;
END
$test$;

-- Duplicate machine codes and duplicate variant codes within a machine fail.
DO $test$
BEGIN
    BEGIN
        INSERT INTO master.machines (code, display_name)
        VALUES ('SH-01-A', 'duplicate smoke-test machine');
        RAISE EXCEPTION 'Duplicate machine code was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;

    BEGIN
        INSERT INTO master.variants (machine_id, code, name)
        SELECT id, '1', 'duplicate smoke-test variant'
          FROM master.machines
         WHERE code = 'SH-01-A';
        RAISE EXCEPTION 'Duplicate variant code within the same machine was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;
END
$test$;

-- Planning uniqueness covers both grouped and ungrouped rows while retaining
-- variant context.
DO $test$
DECLARE
    variant_1_id uuid;
    variant_2_id uuid;
    part_id uuid;
    product_group_id uuid;
BEGIN
    SELECT v.id
      INTO variant_1_id
      FROM master.variants v
      JOIN master.machines m ON m.id = v.machine_id
     WHERE m.code = 'SH-01-A' AND v.code = '1';

    SELECT v.id
      INTO variant_2_id
      FROM master.variants v
      JOIN master.machines m ON m.id = v.machine_id
     WHERE m.code = 'SH-01-A' AND v.code = '2';

    INSERT INTO master.parts (part_code, description)
    VALUES ('__SMOKE_PART__', 'transactional smoke-test part')
    RETURNING id INTO part_id;

    INSERT INTO master.product_groups (variant_id, main_part_id, name)
    VALUES (variant_1_id, part_id, 'transactional smoke-test group')
    RETURNING id INTO product_group_id;

    INSERT INTO master.part_planning_metrics
        (variant_id, product_group_id, part_id, source_note)
    VALUES
        (variant_1_id, product_group_id, part_id, 'grouped uniqueness smoke test');

    BEGIN
        INSERT INTO master.part_planning_metrics
            (variant_id, product_group_id, part_id, source_note)
        VALUES
            (variant_1_id, product_group_id, part_id, 'duplicate grouped row');
        RAISE EXCEPTION 'Duplicate grouped planning metric was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;

    INSERT INTO master.part_planning_metrics
        (variant_id, product_group_id, part_id, source_note)
    VALUES
        (variant_1_id, NULL, part_id, 'ungrouped uniqueness smoke test');

    BEGIN
        INSERT INTO master.part_planning_metrics
            (variant_id, product_group_id, part_id, source_note)
        VALUES
            (variant_1_id, NULL, part_id, 'duplicate ungrouped row');
        RAISE EXCEPTION 'Duplicate ungrouped planning metric was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;

    -- The same ungrouped part remains valid in a different variant.
    INSERT INTO master.part_planning_metrics
        (variant_id, product_group_id, part_id, source_note)
    VALUES
        (variant_2_id, NULL, part_id, 'same part in a different variant');
END
$test$;

-- Reusable resource tables have no variant ownership or direct layout/profile link.
-- The obsolete planning UNIQUE constraint is absent; only the partial indexes remain.
DO $test$
BEGIN
    IF to_regclass('master.layouts') IS NULL THEN
        RAISE EXCEPTION 'master.layouts does not exist';
    END IF;

    IF to_regclass('master.variant_layouts') IS NOT NULL THEN
        RAISE EXCEPTION 'Obsolete master.variant_layouts table still exists';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM information_schema.columns
         WHERE table_schema = 'master'
           AND table_name = 'navigation_profiles'
           AND column_name = 'variant_id'
    ) THEN
        RAISE EXCEPTION 'navigation_profiles still has variant ownership';
    END IF;

    IF (
        SELECT count(*)
          FROM information_schema.columns
         WHERE table_schema = 'master'
           AND table_name = 'navigation_profiles'
           AND column_name IN (
               'base_map_name',
               'robot_map_name',
               'route_profile_name',
               'stitch_profile_name'
           )
    ) <> 4 OR EXISTS (
        SELECT 1
          FROM information_schema.columns
         WHERE table_schema = 'master'
           AND table_name = 'navigation_profiles'
           AND column_name IN (
               'base_map_key',
               'seer_map_key',
               'route_profile_key',
               'stitch_profile_key'
           )
    ) THEN
        RAISE EXCEPTION 'navigation_profiles does not use the canonical map/profile name columns';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM information_schema.columns
         WHERE table_schema = 'master'
           AND table_name = 'layouts'
           AND column_name IN ('variant_id', 'navigation_profile_id')
    ) THEN
        RAISE EXCEPTION 'layouts has variant ownership or a direct navigation-profile link';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid = 'master.part_planning_metrics'::regclass
           AND conname = 'uq_planning_group_part'
    ) THEN
        RAISE EXCEPTION 'Obsolete planning UNIQUE constraint still exists';
    END IF;

    IF (
        SELECT count(*)
          FROM pg_indexes
         WHERE schemaname = 'master'
           AND tablename = 'part_planning_metrics'
           AND indexname IN (
               'uq_planning_group_part',
               'uq_planning_variant_part_without_group'
           )
    ) <> 2 THEN
        RAISE EXCEPTION 'Expected planning partial unique indexes are missing';
    END IF;
END
$test$;

-- Layouts and navigation profiles are machine-scoped reusable resources.
-- Two variants may share them, published versions are unique per logical code,
-- defaults are unique per variant, and cross-machine assignments are rejected.
DO $test$
DECLARE
    machine_a_id uuid;
    machine_b_id uuid;
    variant_a_id uuid;
    variant_b_id uuid;
    variant_other_machine_id uuid;
    shared_layout_id uuid;
    draft_layout_id uuid;
    other_machine_layout_id uuid;
    shared_profile_id uuid;
    draft_profile_id uuid;
    other_machine_profile_id uuid;
    layout_slot_id uuid;
    assignment_count integer;
    distinct_resource_count integer;
BEGIN
    INSERT INTO master.machines (code, display_name)
    VALUES ('__SMOKE_MACHINE_A__', 'transactional smoke-test machine A')
    RETURNING id INTO machine_a_id;

    INSERT INTO master.machines (code, display_name)
    VALUES ('__SMOKE_MACHINE_B__', 'transactional smoke-test machine B')
    RETURNING id INTO machine_b_id;

    INSERT INTO master.variants (machine_id, code, name)
    VALUES (machine_a_id, 'A', 'transactional Variant A')
    RETURNING id INTO variant_a_id;

    INSERT INTO master.variants (machine_id, code, name)
    VALUES (machine_a_id, 'B', 'transactional Variant B')
    RETURNING id INTO variant_b_id;

    INSERT INTO master.variants (machine_id, code, name)
    VALUES (machine_b_id, 'OTHER', 'transactional other-machine variant')
    RETURNING id INTO variant_other_machine_id;

    INSERT INTO master.layouts
        (machine_id, code, name, version, status, published_at)
    VALUES
        (machine_a_id, '__SMOKE_SHARED_LAYOUT__', 'shared layout smoke test', 1, 'PUBLISHED', now())
    RETURNING id INTO shared_layout_id;

    INSERT INTO master.variant_layout_assignments
        (variant_id, machine_id, layout_id, is_default)
    VALUES
        (variant_a_id, machine_a_id, shared_layout_id, true),
        (variant_b_id, machine_a_id, shared_layout_id, true);

    SELECT count(*), count(DISTINCT layout_id)
      INTO assignment_count, distinct_resource_count
      FROM master.variant_layout_assignments
     WHERE variant_id IN (variant_a_id, variant_b_id);

    IF assignment_count <> 2 OR distinct_resource_count <> 1 THEN
        RAISE EXCEPTION 'Variants A and B did not share exactly one layout resource';
    END IF;

    INSERT INTO master.navigation_profiles
        (machine_id, code, name, version, status, published_at)
    VALUES
        (machine_a_id, '__SMOKE_SHARED_PROFILE__', 'shared profile smoke test', 1, 'PUBLISHED', now())
    RETURNING id INTO shared_profile_id;

    INSERT INTO master.variant_navigation_profile_assignments
        (variant_id, machine_id, navigation_profile_id, is_default)
    VALUES
        (variant_a_id, machine_a_id, shared_profile_id, true),
        (variant_b_id, machine_a_id, shared_profile_id, true);

    SELECT count(*), count(DISTINCT navigation_profile_id)
      INTO assignment_count, distinct_resource_count
      FROM master.variant_navigation_profile_assignments
     WHERE variant_id IN (variant_a_id, variant_b_id);

    IF assignment_count <> 2 OR distinct_resource_count <> 1 THEN
        RAISE EXCEPTION 'Variants A and B did not share exactly one navigation profile';
    END IF;

    -- Another DRAFT version of the same logical resource is valid.
    INSERT INTO master.layouts
        (machine_id, code, name, version, status)
    VALUES
        (machine_a_id, '__SMOKE_SHARED_LAYOUT__', 'draft layout version', 2, 'DRAFT')
    RETURNING id INTO draft_layout_id;

    INSERT INTO master.navigation_profiles
        (machine_id, code, name, version, status)
    VALUES
        (machine_a_id, '__SMOKE_SHARED_PROFILE__', 'draft profile version', 2, 'DRAFT')
    RETURNING id INTO draft_profile_id;

    BEGIN
        INSERT INTO master.layouts
            (machine_id, code, name, version, status, published_at)
        VALUES
            (machine_a_id, '__SMOKE_SHARED_LAYOUT__', 'second published layout', 3, 'PUBLISHED', now());
        RAISE EXCEPTION 'A second PUBLISHED layout version was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;

    BEGIN
        INSERT INTO master.navigation_profiles
            (machine_id, code, name, version, status, published_at)
        VALUES
            (machine_a_id, '__SMOKE_SHARED_PROFILE__', 'second published profile', 3, 'PUBLISHED', now());
        RAISE EXCEPTION 'A second PUBLISHED navigation-profile version was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;

    BEGIN
        INSERT INTO master.variant_layout_assignments
            (variant_id, machine_id, layout_id, is_default)
        VALUES
            (variant_a_id, machine_a_id, draft_layout_id, true);
        RAISE EXCEPTION 'A second default layout assignment was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;

    BEGIN
        INSERT INTO master.variant_navigation_profile_assignments
            (variant_id, machine_id, navigation_profile_id, is_default)
        VALUES
            (variant_a_id, machine_a_id, draft_profile_id, true);
        RAISE EXCEPTION 'A second default navigation-profile assignment was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;

    INSERT INTO master.layouts
        (machine_id, code, name, version, status, published_at)
    VALUES
        (machine_b_id, '__SMOKE_OTHER_LAYOUT__', 'other-machine layout', 1, 'PUBLISHED', now())
    RETURNING id INTO other_machine_layout_id;

    INSERT INTO master.navigation_profiles
        (machine_id, code, name, version, status, published_at)
    VALUES
        (machine_b_id, '__SMOKE_OTHER_PROFILE__', 'other-machine profile', 1, 'PUBLISHED', now())
    RETURNING id INTO other_machine_profile_id;

    BEGIN
        INSERT INTO master.variant_layout_assignments
            (variant_id, machine_id, layout_id, is_default)
        VALUES
            (variant_a_id, machine_a_id, other_machine_layout_id, false);
        RAISE EXCEPTION 'A cross-machine layout assignment was accepted';
    EXCEPTION
        WHEN foreign_key_violation THEN NULL;
    END;

    BEGIN
        INSERT INTO master.variant_navigation_profile_assignments
            (variant_id, machine_id, navigation_profile_id, is_default)
        VALUES
            (variant_a_id, machine_a_id, other_machine_profile_id, false);
        RAISE EXCEPTION 'A cross-machine navigation-profile assignment was accepted';
    EXCEPTION
        WHEN foreign_key_violation THEN NULL;
    END;

    -- Invalid physical/load-state combinations fail; an occupied/full slot is valid.
    INSERT INTO master.layout_slots
        (layout_id, code, display_name, slot_role)
    VALUES
        (shared_layout_id, '__SMOKE_SLOT__', 'transactional smoke-test slot', 'MACHINE')
    RETURNING id INTO layout_slot_id;

    BEGIN
        INSERT INTO ops.slot_runtime_state
            (layout_slot_id, occupancy_state, load_state)
        VALUES
            (layout_slot_id, 'VACANT', 'EMPTY');
        RAISE EXCEPTION 'VACANT/EMPTY slot state was accepted';
    EXCEPTION
        WHEN check_violation THEN NULL;
    END;

    BEGIN
        INSERT INTO ops.slot_runtime_state
            (layout_slot_id, occupancy_state, load_state)
        VALUES
            (layout_slot_id, 'UNKNOWN', 'FULL');
        RAISE EXCEPTION 'UNKNOWN/FULL slot state was accepted';
    EXCEPTION
        WHEN check_violation THEN NULL;
    END;

    INSERT INTO ops.slot_runtime_state
        (layout_slot_id, occupancy_state, load_state)
    VALUES
        (layout_slot_id, 'OCCUPIED', 'FULL');
END
$test$;

ROLLBACK;
