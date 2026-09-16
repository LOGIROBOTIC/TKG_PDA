-- Proves the dispatch source of a BRING_FULL resolves from the pallet standing
-- in a preparation area, and stays unresolved in every case where it should.
--
-- This exercises the same resolution the `prep` lateral in
-- PostgresRdsExecutionStore.GetDispatchContextAsync performs. It cannot be run
-- through the API: dispatch is globally disabled in development, so a real
-- dispatch attempt is refused before the source is ever resolved.
--
-- Everything rolls back, including the area-to-worksite mapping, because which
-- physical point each preparation area occupies is the customer's decision and
-- is not invented here.
\set ON_ERROR_STOP off
BEGIN;

CREATE TEMP TABLE t AS
SELECT (SELECT id FROM ops.preparation_areas WHERE area_number = 1) AS area,
       (SELECT id FROM auth.users WHERE employee_no = 'DEV-WH')     AS usr,
       (SELECT id FROM audit.client_devices WHERE device_code = 'DEV-WAREHOUSE') AS dev,
       (SELECT r.id FROM ops.transport_requests r
        JOIN master.task_types tt ON tt.id = r.task_type_id
        WHERE tt.code = 'BRING_FULL' ORDER BY r.request_no DESC LIMIT 1) AS req;

SELECT CASE WHEN req IS NULL
            THEN 'UYARI: hic BRING_FULL talebi yok, test anlamsiz'
            ELSE 'BRING_FULL talebi bulundu' END AS durum
FROM t;

-- The worksite must belong to the request's own navigation profile: the same
-- floor position carries a different point name in each map.
CREATE TEMP TABLE w AS
SELECT rw.id AS worksite, rw.rds_site_id AS site, r.navigation_id AS profile
FROM ops.transport_requests r
JOIN integration.rds_worksites rw ON rw.navigation_profile_id = r.navigation_id
WHERE r.id = (SELECT req FROM t)
ORDER BY rw.rds_site_id
LIMIT 1;

-- The query under test, factored out so each case below reads the same way.
CREATE OR REPLACE FUNCTION pg_temp.resolved_source(p_request uuid)
RETURNS TABLE (slot_code varchar, slot_role varchar, site_id varchar)
LANGUAGE sql AS $$
    SELECT pa.display_name, 'SHARED'::varchar, rw.rds_site_id
    FROM ops.transport_requests r
    JOIN master.task_types tt ON tt.id = r.task_type_id
    JOIN ops.preparation_pallets pp ON pp.transport_request_id = r.id
    JOIN ops.preparation_areas pa ON pa.id = pp.preparation_area_id AND pa.is_enabled
    JOIN integration.preparation_area_rds_mapping pm ON pm.preparation_area_id = pa.id
    JOIN integration.rds_worksites rw ON rw.id = pm.rds_worksite_id
                                     AND rw.navigation_profile_id = r.navigation_id
    WHERE r.id = p_request
      AND tt.code = 'BRING_FULL'
      AND pp.released_at IS NULL
      AND pp.ready_at IS NOT NULL
    LIMIT 1;
$$;

\echo ''
\echo '=== 1. esleme yok, palet yok -> kaynak cozulmemeli ==='
SELECT count(*) AS cozulen FROM pg_temp.resolved_source((SELECT req FROM t));

\echo ''
\echo '=== 2. alan bir RDS noktasina eslenir ==='
INSERT INTO integration.preparation_area_rds_mapping (preparation_area_id, rds_worksite_id)
SELECT (SELECT area FROM t), worksite FROM w;
SELECT site AS eslenen_nokta FROM w;

\echo ''
\echo '=== 3. esleme var ama palet yok -> hala cozulmemeli ==='
SELECT count(*) AS cozulen FROM pg_temp.resolved_source((SELECT req FROM t));

\echo ''
\echo '=== 4. palet konur ama HAZIR degil -> hala cozulmemeli ==='
INSERT INTO ops.preparation_pallets
    (preparation_area_id, container_type_id, load_state, part_id,
     transport_request_id, placed_by, placed_device_id)
SELECT t.area, r.container_type_id, 'FULL', r.part_id, r.id, t.usr, t.dev
FROM t JOIN ops.transport_requests r ON r.id = t.req;
SELECT count(*) AS cozulen FROM pg_temp.resolved_source((SELECT req FROM t));

\echo ''
\echo '=== 5. palet HAZIR isaretlenir -> kaynak cozulmeli ==='
UPDATE ops.preparation_pallets
SET ready_at = clock_timestamp(), ready_by = (SELECT usr FROM t)
WHERE transport_request_id = (SELECT req FROM t) AND released_at IS NULL;
SELECT slot_code AS alan, slot_role AS rol, site_id AS nokta
FROM pg_temp.resolved_source((SELECT req FROM t));

\echo ''
\echo '=== 6. palet toplanip alan bosalir -> kaynak tekrar cozulmemeli ==='
UPDATE ops.preparation_pallets
SET released_at = clock_timestamp(), released_by = (SELECT usr FROM t), released_reason = 'COLLECTED'
WHERE transport_request_id = (SELECT req FROM t) AND released_at IS NULL;
SELECT count(*) AS cozulen FROM pg_temp.resolved_source((SELECT req FROM t));

ROLLBACK;
