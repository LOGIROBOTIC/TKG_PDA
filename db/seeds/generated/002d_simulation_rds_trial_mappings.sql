-- 002d_simulation_rds_trial_mappings.sql
-- Trial mappings between Roboshop / SEER simulation map (26.7.2026_map_optimak.smap)
-- and SH-01-A CUSTOMER-PHYSICAL-V1 layout for end-to-end commissioning validation.
-- Can be updated or dropped at any time without impacting customer master data.

-- 1. Register ActionPoints as Mirrored WorkSites under DEV-RDS-SIM navigation profile
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

-- 2. Ensure BUFFER-001 (EMPTY_STORAGE) and DROP-001 (DROP) slots exist on SH-01-A layout
INSERT INTO master.layout_slots (
    id, layout_id, code, display_name, slot_role, default_container_type_id,
    ui_x, ui_y, ui_width, ui_height, display_order, allows_empty_load, allows_full_load, is_enabled, metadata_json
)
VALUES
    ('c2000000-0000-0000-0000-000000000001', 'ae8d65cb-53fa-434a-bbc8-891c5443bfa1', 'BUFFER-001', 'Depo Boş Kasa Buffer (AP21)', 'EMPTY_STORAGE', '2694fe17-d871-4613-a406-7f84537a5f09', 10, 10, 50, 50, 90, true, false, true, '{}'),
    ('c2000000-0000-0000-0000-000000000002', 'ae8d65cb-53fa-434a-bbc8-891c5443bfa1', 'DROP-001', 'Dolu Kasa Teslim (AP4)', 'DROP', '2694fe17-d871-4613-a406-7f84537a5f09', 70, 10, 50, 50, 91, false, true, true, '{}')
ON CONFLICT (layout_id, code) DO UPDATE SET
    display_name = EXCLUDED.display_name,
    slot_role = EXCLUDED.slot_role;

-- 3. Runtime state for buffer and drop slots
INSERT INTO ops.slot_runtime_state (layout_slot_id, occupancy_state, load_state, row_version, updated_at)
VALUES
    ('c2000000-0000-0000-0000-000000000001', 'OCCUPIED', 'EMPTY', 0, now()),
    ('c2000000-0000-0000-0000-000000000002', 'VACANT', 'UNKNOWN', 0, now())
ON CONFLICT (layout_slot_id) DO UPDATE SET
    occupancy_state = EXCLUDED.occupancy_state,
    load_state = EXCLUDED.load_state;

-- 4. Map Layout Slots to RDS WorkSites
INSERT INTO integration.layout_slot_rds_mapping (layout_slot_id, rds_worksite_id)
VALUES
    ('c2000000-0000-0000-0000-000000000001', 'c1000000-0000-0000-0000-000000000001'), -- BUFFER-001 -> AP21 (Depo Çıkış)
    ('5b4dca64-b844-4cc5-b4a1-14c278024eb8', 'c1000000-0000-0000-0000-000000000002'), -- SLOT-001 -> AP7 (Üst hat besleme)
    ('0cb5c749-92df-4a41-b148-c37a52a70177', 'c1000000-0000-0000-0000-000000000003'), -- SLOT-002 -> AP81 (Üst hat besleme 2)
    ('ba221b81-5ea2-434c-8640-8ef7a1505684', 'c1000000-0000-0000-0000-000000000004'), -- SLOT-003 -> AP82 (Üst hat besleme 3)
    ('01ce85ab-0fd9-4499-ab88-4c27ff75004c', 'c1000000-0000-0000-0000-000000000005'), -- SLOT-004 -> AP83
    ('c2000000-0000-0000-0000-000000000002', 'c1000000-0000-0000-0000-000000000006')  -- DROP-001 -> AP4 (Dolu iade teslim)
ON CONFLICT (layout_slot_id) DO UPDATE SET
    rds_worksite_id = EXCLUDED.rds_worksite_id;

-- 5. Task Templates for SH-01-A Variant 1
INSERT INTO master.task_templates (id, variant_id, task_type_id, rds_task_label, priority, config_json, is_active)
VALUES
    ('c3000000-0000-0000-0000-000000000001', '3e5be5cf-2771-432a-a60b-0f87f4f0cf5b', '645dacce-39dd-4c45-9214-422f91de4186', 'TKG_BRING_EMPTY', 1, '{"vehicle":"Sfl-Cbd15"}', true),
    ('c3000000-0000-0000-0000-000000000002', '3e5be5cf-2771-432a-a60b-0f87f4f0cf5b', '4c90d04c-cc8b-4aeb-bc22-58445b829eb5', 'TKG_REMOVE_FULL', 1, '{"vehicle":"Sfl-Cbd15"}', true),
    ('c3000000-0000-0000-0000-000000000003', '3e5be5cf-2771-432a-a60b-0f87f4f0cf5b', '8440a35e-d353-4a43-b60c-8263d9ec42ea', 'TKG_REMOVE_EMPTY', 1, '{"vehicle":"Sfl-Cbd15"}', true)
ON CONFLICT (id) DO UPDATE SET
    rds_task_label = EXCLUDED.rds_task_label,
    config_json = EXCLUDED.config_json,
    is_active = true;

-- 6. Grant permissions to fatih for SH-01-A
INSERT INTO auth.user_machine_permissions (user_id, machine_id)
SELECT u.id, m.id
FROM auth.users u
CROSS JOIN master.machines m
WHERE u.username = 'fatih' AND m.code = 'SH-01-A'
ON CONFLICT DO NOTHING;

-- 7. Activate DEV-RDS-SIM navigation profile on SH-01-A
UPDATE ops.machine_runtime_state
SET active_navigation_id = 'fba61025-221d-4556-9945-865fa8d04ab3'
WHERE machine_id = '2384d0fc-4dea-4174-b477-347fb365f645';
