-- 002d_simulation_rds_trial_mappings.sql
-- Simulation Profile: OPTIMAK_SIM_2026 (Environment: SIMULATION)
-- Configures Roboshop / SEER simulation maps:
--   Map A: 26.7.2026_map_optimak (bound to SH-01-A Variant 1 via DEV-RDS-SIM-MAP-A)
--   Map B: 26.7.2026_map_optimak_1 (bound to SH-01-A Variant 2 via DEV-RDS-SIM-MAP-B)
-- Transition site: SM11
-- Endpoint roles:
--   EMPTY_STORAGE -> BUFFER-001 -> AP21
--   DROP          -> DROP-001   -> AP4
-- Machine slots   -> SLOT-001..SLOT-004 -> AP7, AP81, AP82, AP83
-- NO HARDCODED AP4 or AP21 in task templates.

-- 1. Navigation Profiles for Simulation
INSERT INTO master.navigation_profiles (
    id, machine_id, code, name, version, status, robot_map_name, config_json
)
VALUES
    (
        'fba61025-221d-4556-9945-865fa8d04ab3',
        '2384d0fc-4dea-4174-b477-347fb365f645',
        'DEV-RDS-SIM-MAP-A',
        'DEV-RDS-SIM (Map A - 26.7.2026_map_optimak)',
        1,
        'PUBLISHED',
        '26.7.2026_map_optimak',
        '{"environment":"SIMULATION","scope":"OPTIMAK_SIM_2026","switchTransitionStation":"SM11","vehicle":"Sfl-Cbd15","allowedVehicles":["Sfl-Cbd15"]}'::jsonb
    ),
    (
        'fba61025-221d-4556-9945-865fa8d04ab4',
        '2384d0fc-4dea-4174-b477-347fb365f645',
        'DEV-RDS-SIM-MAP-B',
        'DEV-RDS-SIM (Map B - 26.7.2026_map_optimak_1)',
        1,
        'PUBLISHED',
        '26.7.2026_map_optimak_1',
        '{"environment":"SIMULATION","scope":"OPTIMAK_SIM_2026","switchTransitionStation":"SM11","vehicle":"Sfl-Cbd15","allowedVehicles":["Sfl-Cbd15"]}'::jsonb
    )
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code,
    name = EXCLUDED.name,
    status = EXCLUDED.status,
    robot_map_name = EXCLUDED.robot_map_name,
    config_json = EXCLUDED.config_json;

-- 2. Bind Navigation Profiles to Variants (Variant 1 -> Map A, Variant 2 -> Map B)
INSERT INTO master.variant_navigation_profile_assignments (
    variant_id, machine_id, navigation_profile_id, is_default, assigned_at
)
VALUES
    ('3e5be5cf-2771-432a-a60b-0f87f4f0cf5b', '2384d0fc-4dea-4174-b477-347fb365f645', 'fba61025-221d-4556-9945-865fa8d04ab3', true, now()),
    ('ff128ec0-f870-4a5c-9746-1c409463ae70', '2384d0fc-4dea-4174-b477-347fb365f645', 'fba61025-221d-4556-9945-865fa8d04ab4', true, now())
ON CONFLICT (variant_id, navigation_profile_id) DO UPDATE SET
    is_default = EXCLUDED.is_default,
    assigned_at = now();

-- 3. Register ActionPoints as Mirrored WorkSites under Map A Navigation Profile
INSERT INTO integration.rds_worksites (
    id, navigation_profile_id, rds_site_id, area, group_name, rds_type,
    last_filled, last_locked, last_disabled, last_content, last_agv_id, last_sync_failed, last_sync_at, raw_state_json
)
VALUES
    ('c1000000-0000-0000-0000-000000000001', 'fba61025-221d-4556-9945-865fa8d04ab3', 'AP21', 'DEPOT', 'BUFFER', 1, 0, 0, 0, null, null, 0, now(), '{}'),
    ('c1000000-0000-0000-0000-000000000002', 'fba61025-221d-4556-9945-865fa8d04ab3', 'AP7', 'LINE', 'FEED', 1, 0, 0, 0, null, null, 0, now(), '{}'),
    ('c1000000-0000-0000-0000-000000000003', 'fba61025-221d-4556-9945-865fa8d04ab3', 'AP81', 'LINE', 'FEED', 1, 0, 0, 0, null, null, 0, now(), '{}'),
    ('c1000000-0000-0000-0000-000000000004', 'fba61025-221d-4556-9945-865fa8d04ab3', 'AP82', 'LINE', 'FEED', 1, 0, 0, 0, null, null, 0, now(), '{}'),
    ('c1000000-0000-0000-0000-000000000005', 'fba61025-221d-4556-9945-865fa8d04ab3', 'AP83', 'LINE', 'FEED', 1, 0, 0, 0, null, null, 0, now(), '{}'),
    ('c1000000-0000-0000-0000-000000000006', 'fba61025-221d-4556-9945-865fa8d04ab3', 'AP4', 'WAREHOUSE', 'DROP', 1, 0, 0, 0, null, null, 0, now(), '{}'),
    ('c1000000-0000-0000-0000-000000000007', 'fba61025-221d-4556-9945-865fa8d04ab3', 'AP1', 'WAREHOUSE', 'STORAGE', 1, 0, 0, 0, null, null, 0, now(), '{}'),
    ('c1000000-0000-0000-0000-000000000008', 'fba61025-221d-4556-9945-865fa8d04ab3', 'PP80', 'PARK', 'PARK', 1, 0, 0, 0, null, null, 0, now(), '{}')
ON CONFLICT (id) DO UPDATE SET 
    rds_site_id = EXCLUDED.rds_site_id,
    area = EXCLUDED.area,
    group_name = EXCLUDED.group_name,
    last_sync_at = now();

-- 4. Ensure BUFFER-001 (EMPTY_STORAGE) and DROP-001 (DROP) slots exist on Variant 1 and Variant 2 layouts
INSERT INTO master.layout_slots (
    id, layout_id, code, display_name, slot_role, default_container_type_id,
    ui_x, ui_y, ui_width, ui_height, display_order, allows_empty_load, allows_full_load, is_enabled, metadata_json
)
VALUES
    -- Variant 1 layout (ae8d65cb-53fa-434a-bbc8-891c5443bfa1)
    ('c2000000-0000-0000-0000-000000000001', 'ae8d65cb-53fa-434a-bbc8-891c5443bfa1', 'BUFFER-001', 'Depo Boş Kasa Buffer (AP21)', 'EMPTY_STORAGE', '2694fe17-d871-4613-a406-7f84537a5f09', 10, 10, 50, 50, 90, true, false, true, '{"scope":"OPTIMAK_SIM_2026"}'),
    ('c2000000-0000-0000-0000-000000000002', 'ae8d65cb-53fa-434a-bbc8-891c5443bfa1', 'DROP-001', 'Dolu Kasa Teslim (AP4)', 'DROP', '2694fe17-d871-4613-a406-7f84537a5f09', 70, 10, 50, 50, 91, false, true, true, '{"scope":"OPTIMAK_SIM_2026"}'),
    -- Variant 2 layout (50708695-1c28-4add-9d0e-d58354d48be7)
    ('c2000000-0000-0000-0000-000000000003', '50708695-1c28-4add-9d0e-d58354d48be7', 'BUFFER-001', 'Depo Boş Kasa Buffer (AP21)', 'EMPTY_STORAGE', '2694fe17-d871-4613-a406-7f84537a5f09', 10, 10, 50, 50, 90, true, false, true, '{"scope":"OPTIMAK_SIM_2026"}'),
    ('c2000000-0000-0000-0000-000000000004', '50708695-1c28-4add-9d0e-d58354d48be7', 'DROP-001', 'Dolu Kasa Teslim (AP4)', 'DROP', '2694fe17-d871-4613-a406-7f84537a5f09', 70, 10, 50, 50, 91, false, true, true, '{"scope":"OPTIMAK_SIM_2026"}')
ON CONFLICT (layout_id, code) DO UPDATE SET
    display_name = EXCLUDED.display_name,
    slot_role = EXCLUDED.slot_role;

-- 5. Runtime state for buffer and drop slots
INSERT INTO ops.slot_runtime_state (layout_slot_id, occupancy_state, load_state, row_version, updated_at)
VALUES
    ('c2000000-0000-0000-0000-000000000001', 'OCCUPIED', 'EMPTY', 0, now()),
    ('c2000000-0000-0000-0000-000000000002', 'VACANT', 'UNKNOWN', 0, now()),
    ('c2000000-0000-0000-0000-000000000003', 'OCCUPIED', 'EMPTY', 0, now()),
    ('c2000000-0000-0000-0000-000000000004', 'VACANT', 'UNKNOWN', 0, now())
ON CONFLICT (layout_slot_id) DO UPDATE SET
    occupancy_state = EXCLUDED.occupancy_state,
    load_state = EXCLUDED.load_state;

-- 6. Map Layout Slots to RDS WorkSites (Simulation overlay)
INSERT INTO integration.layout_slot_rds_mapping (layout_slot_id, rds_worksite_id)
VALUES
    -- Variant 1 layout slots
    ('c2000000-0000-0000-0000-000000000001', 'c1000000-0000-0000-0000-000000000001'), -- BUFFER-001 -> AP21
    ('5b4dca64-b844-4cc5-b4a1-14c278024eb8', 'c1000000-0000-0000-0000-000000000002'), -- V1 SLOT-001 -> AP7
    ('0cb5c749-92df-4a41-b148-c37a52a70177', 'c1000000-0000-0000-0000-000000000003'), -- V1 SLOT-002 -> AP81
    ('ba221b81-5ea2-434c-8640-8ef7a1505684', 'c1000000-0000-0000-0000-000000000004'), -- V1 SLOT-003 -> AP82
    ('01ce85ab-0fd9-4499-ab88-4c27ff75004c', 'c1000000-0000-0000-0000-000000000005'), -- V1 SLOT-004 -> AP83
    ('c2000000-0000-0000-0000-000000000002', 'c1000000-0000-0000-0000-000000000006'), -- V1 DROP-001 -> AP4
    -- Variant 2 layout slots
    ('c2000000-0000-0000-0000-000000000003', 'c1000000-0000-0000-0000-000000000001'), -- V2 BUFFER-001 -> AP21
    ('3b5861f0-cb9b-4e5b-9af1-3b380122ff12', 'c1000000-0000-0000-0000-000000000002'), -- V2 SLOT-001 -> AP7
    ('9a9e4c80-f6cb-4ef3-8de0-2ba66e08c2fc', 'c1000000-0000-0000-0000-000000000003'), -- V2 SLOT-002 -> AP81
    ('017ef8a6-0879-47eb-996c-a6a850ea862d', 'c1000000-0000-0000-0000-000000000004'), -- V2 SLOT-003 -> AP82
    ('1695f220-a739-4be2-b5cb-cdbb79145c7c', 'c1000000-0000-0000-0000-000000000005'), -- V2 SLOT-004 -> AP83
    ('c2000000-0000-0000-0000-000000000004', 'c1000000-0000-0000-0000-000000000006')  -- V2 DROP-001 -> AP4
ON CONFLICT (layout_slot_id) DO UPDATE SET
    rds_worksite_id = EXCLUDED.rds_worksite_id;

-- 7. Task Templates for SH-01-A Variant 1 & Variant 2 (Zero hardcoded AP4/locations)
INSERT INTO master.task_templates (id, variant_id, task_type_id, rds_task_label, priority, config_json, is_active)
VALUES
    -- Variant 1 templates
    ('c3000000-0000-0000-0000-000000000001', '3e5be5cf-2771-432a-a60b-0f87f4f0cf5b', '645dacce-39dd-4c45-9214-422f91de4186', 'TKG_BRING_EMPTY', 1, '{"executionMode":"RDSCORE_NATIVE_ORDER"}'::jsonb, true),
    ('c3000000-0000-0000-0000-000000000002', '3e5be5cf-2771-432a-a60b-0f87f4f0cf5b', '4c90d04c-cc8b-4aeb-bc22-58445b829eb5', 'TKG_REMOVE_FULL', 1, '{"executionMode":"RDSCORE_NATIVE_ORDER"}'::jsonb, true),
    ('c3000000-0000-0000-0000-000000000003', '3e5be5cf-2771-432a-a60b-0f87f4f0cf5b', '8440a35e-d353-4a43-b60c-8263d9ec42ea', 'TKG_REMOVE_EMPTY', 1, '{"executionMode":"RDSCORE_NATIVE_ORDER"}'::jsonb, true),
    -- Variant 2 templates
    ('c3000000-0000-0000-0000-000000000004', 'ff128ec0-f870-4a5c-9746-1c409463ae70', '645dacce-39dd-4c45-9214-422f91de4186', 'TKG_BRING_EMPTY', 1, '{"executionMode":"RDSCORE_NATIVE_ORDER"}'::jsonb, true),
    ('c3000000-0000-0000-0000-000000000005', 'ff128ec0-f870-4a5c-9746-1c409463ae70', '4c90d04c-cc8b-4aeb-bc22-58445b829eb5', 'TKG_REMOVE_FULL', 1, '{"executionMode":"RDSCORE_NATIVE_ORDER"}'::jsonb, true),
    ('c3000000-0000-0000-0000-000000000006', 'ff128ec0-f870-4a5c-9746-1c409463ae70', '8440a35e-d353-4a43-b60c-8263d9ec42ea', 'TKG_REMOVE_EMPTY', 1, '{"executionMode":"RDSCORE_NATIVE_ORDER"}'::jsonb, true)
ON CONFLICT (id) DO UPDATE SET
    rds_task_label = EXCLUDED.rds_task_label,
    config_json = EXCLUDED.config_json,
    is_active = true;

-- 8. Grant permissions to fatih for SH-01-A
INSERT INTO auth.user_machine_permissions (user_id, machine_id)
SELECT u.id, m.id
FROM auth.users u
CROSS JOIN master.machines m
WHERE u.username = 'fatih' AND m.code = 'SH-01-A'
ON CONFLICT DO NOTHING;

-- 9. Initial active Navigation Profile on SH-01-A runtime state
UPDATE ops.machine_runtime_state
SET active_navigation_id = 'fba61025-221d-4556-9945-865fa8d04ab3'
WHERE machine_id = '2384d0fc-4dea-4174-b477-347fb365f645';

