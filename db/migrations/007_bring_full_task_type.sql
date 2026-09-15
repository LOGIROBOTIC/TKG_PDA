-- Fourth transport operation: warehouse feeds prepared material to a machine.
-- Forward-only addition after 006. The three existing operations keep their
-- code, name and meaning; no request, slot state or mapping is rewritten.
--
-- master.task_types is a lookup table with no CHECK constraint on code, so this
-- is a data insert rather than a schema change. The operation is inert until a
-- task template references it: TkgPda.Application refuses to dispatch a
-- BRING_FULL whose source is unresolved, and the shared preparation areas that
-- will supply that source do not exist yet.
BEGIN;

INSERT INTO master.task_types (code, name)
VALUES ('BRING_FULL', 'Dolu Palet/Kasa Getir')
ON CONFLICT (code) DO UPDATE
SET name = EXCLUDED.name
WHERE master.task_types.name IS DISTINCT FROM EXCLUDED.name;

COMMIT;
