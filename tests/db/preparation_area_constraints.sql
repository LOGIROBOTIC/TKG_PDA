-- Prove the preparation-area rules are held by the database, not by whichever
-- caller happens to check first. Each expected failure sits in its own
-- savepoint so one rejection does not abort the rest. Everything rolls back.
\set ON_ERROR_STOP off
BEGIN;

CREATE TEMP TABLE t AS
SELECT (SELECT id FROM ops.preparation_areas WHERE area_number = 1) AS area1,
       (SELECT id FROM ops.preparation_areas WHERE area_number = 2) AS area2,
       (SELECT id FROM master.container_types ORDER BY code LIMIT 1)  AS container,
       (SELECT id FROM master.parts ORDER BY part_code LIMIT 1)       AS part,
       (SELECT id FROM auth.users ORDER BY created_at LIMIT 1)        AS usr,
       (SELECT id FROM audit.client_devices ORDER BY device_code LIMIT 1) AS dev,
       (SELECT id FROM ops.transport_requests ORDER BY request_no LIMIT 1) AS req;

\echo ''
\echo '=== 1. bos kap, talep olmadan (BEKLENEN: kabul) ==='
SAVEPOINT s1;
INSERT INTO ops.preparation_pallets
    (preparation_area_id, container_type_id, load_state, placed_by, placed_device_id)
SELECT area1, container, 'EMPTY', usr, dev FROM t;

\echo ''
\echo '=== 2. ayni alana ikinci palet (BEKLENEN: RET) ==='
SAVEPOINT s2;
INSERT INTO ops.preparation_pallets
    (preparation_area_id, container_type_id, load_state, placed_by, placed_device_id)
SELECT area1, container, 'EMPTY', usr, dev FROM t;
ROLLBACK TO SAVEPOINT s2;

\echo ''
\echo '=== 3. bos palet icinde malzeme (BEKLENEN: RET) ==='
SAVEPOINT s3;
INSERT INTO ops.preparation_pallets
    (preparation_area_id, container_type_id, load_state, part_id, placed_by, placed_device_id)
SELECT area2, container, 'EMPTY', part, usr, dev FROM t;
ROLLBACK TO SAVEPOINT s3;

\echo ''
\echo '=== 4. yari mamul talepsiz hazirlanamaz (BEKLENEN: RET) ==='
SAVEPOINT s4;
INSERT INTO ops.preparation_pallets
    (preparation_area_id, container_type_id, load_state, part_id, placed_by, placed_device_id)
SELECT area2, container, 'FULL', part, usr, dev FROM t;
ROLLBACK TO SAVEPOINT s4;

\echo ''
\echo '=== 5. dolu palet parcasiz (BEKLENEN: RET) ==='
SAVEPOINT s5;
INSERT INTO ops.preparation_pallets
    (preparation_area_id, container_type_id, load_state, transport_request_id, placed_by, placed_device_id)
SELECT area2, container, 'FULL', req, usr, dev FROM t;
ROLLBACK TO SAVEPOINT s5;

\echo ''
\echo '=== 6. dolu palet, talep ve parca ile (BEKLENEN: kabul) ==='
SAVEPOINT s6;
INSERT INTO ops.preparation_pallets
    (preparation_area_id, container_type_id, load_state, part_id, transport_request_id, placed_by, placed_device_id)
SELECT area2, container, 'FULL', part, req, usr, dev FROM t;

\echo ''
\echo '=== 7. hazir onayi olmadan toplandi (BEKLENEN: RET) ==='
SAVEPOINT s7;
UPDATE ops.preparation_pallets
SET released_at = clock_timestamp(),
    released_by = (SELECT usr FROM t),
    released_reason = 'COLLECTED'
WHERE preparation_area_id = (SELECT area2 FROM t) AND released_at IS NULL;
ROLLBACK TO SAVEPOINT s7;

\echo ''
\echo '=== 8. ayni talep icin ikinci alanda palet (BEKLENEN: RET) ==='
SAVEPOINT s8;
INSERT INTO ops.preparation_pallets
    (preparation_area_id, container_type_id, load_state, part_id, transport_request_id, placed_by, placed_device_id)
SELECT (SELECT id FROM ops.preparation_areas WHERE area_number = 3), container, 'FULL', part, req, usr, dev FROM t;
ROLLBACK TO SAVEPOINT s8;

\echo ''
\echo '=== 9. once hazir sonra toplandi (BEKLENEN: kabul) ==='
SAVEPOINT s9;
UPDATE ops.preparation_pallets
SET ready_at = clock_timestamp(), ready_by = (SELECT usr FROM t)
WHERE preparation_area_id = (SELECT area2 FROM t) AND released_at IS NULL;
UPDATE ops.preparation_pallets
SET released_at = clock_timestamp(),
    released_by = (SELECT usr FROM t),
    released_reason = 'COLLECTED'
WHERE preparation_area_id = (SELECT area2 FROM t) AND released_at IS NULL;

\echo ''
\echo '=== 10. bosalan alana yeni palet (BEKLENEN: kabul) ==='
SAVEPOINT s10;
INSERT INTO ops.preparation_pallets
    (preparation_area_id, container_type_id, load_state, placed_by, placed_device_id)
SELECT area2, container, 'EMPTY', usr, dev FROM t;

\echo ''
\echo '=== SONUC ==='
SELECT pa.area_number AS alan,
       pp.load_state AS yuk,
       (pp.part_id IS NOT NULL) AS malzeme,
       (pp.transport_request_id IS NOT NULL) AS talepli,
       (pp.ready_at IS NOT NULL) AS hazir,
       coalesce(pp.released_reason, '-') AS birakma,
       (pp.released_at IS NULL) AS aktif
FROM ops.preparation_pallets pp
JOIN ops.preparation_areas pa ON pa.id = pp.preparation_area_id
ORDER BY pa.area_number, pp.placed_at;

ROLLBACK;
