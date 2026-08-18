# TKG PDA Phase 01 Notes

**Date:** 2026-08-18
**Scope:** Framework-neutral PostgreSQL database baseline only

## Implemented baseline

- Added the canonical database specification under `docs/`.
- Added the initial PostgreSQL migration under `db/migrations/`.
- Added the RDS public API reference under `docs/references/rds/`.
- Added seed-policy documentation under `db/seeds/`.
- Added a transactional schema smoke test under `tests/db/`.
- Did not add a PDA UI, backend API, ORM, PLC integration, SAP integration, RDS client or SEER map-switching implementation.

## SQL defect corrected: nullable planning uniqueness

The supplied migration used:

```sql
CONSTRAINT uq_planning_group_part UNIQUE (product_group_id, part_id)
```

This was incorrect for the intended model because:

1. The same part may occur in different product groups and variants.
2. `product_group_id` is nullable.
3. PostgreSQL treats `NULL` values as distinct in a normal unique constraint, so duplicate ungrouped planning rows would not be rejected.

The table constraint was replaced with two partial unique indexes:

```sql
CREATE UNIQUE INDEX uq_planning_group_part
ON master.part_planning_metrics (variant_id, product_group_id, part_id)
WHERE product_group_id IS NOT NULL;

CREATE UNIQUE INDEX uq_planning_variant_part_without_group
ON master.part_planning_metrics (variant_id, part_id)
WHERE product_group_id IS NULL;
```

This preserves reuse of the same part across variants/product groups while preventing duplicate planning rows in the same business context.

## Shared layout and navigation-profile decision

The combined SH-01-P1/P2 and SH-02-P1/P2 workbook establishes two production variants per machine but supplies only one common physical plan. No second layout or navigation profile was invented.

The source-backed production identities remain separate:

- SH-01-P1/P2 Variant 1: `71550BN000-10-HAOS`; Variant 2: `71560BN000-10-HAOS`.
- SH-02-P1/P2 Variant 1: `71550BN400-10-HMTR`; Variant 2: `71560BN400-10-HMTR`.

To preserve this source truth:

- `master.layouts` and `master.navigation_profiles` are machine-scoped reusable definitions.
- `master.variant_layout_assignments` maps variants to layouts.
- `master.variant_navigation_profile_assignments` maps variants to navigation profiles.
- A partial unique index permits at most one default layout/profile assignment per variant.
- Multiple variants can point to the same layout or navigation profile.

If the customer later supplies a distinct Variant 2 plan, its assignment can be changed to a new definition without changing the variant identity.

## Phase 01.1 schema-consistency review

The final Phase 01 migration already had no `variant_id` in `master.navigation_profiles`, and its physical-layout table already had neither `variant_id` nor `navigation_profile_id`. Phase 01.1 completed the model consistently instead of relying on the earlier completion-report excerpt:

- Renamed `master.variant_layouts` to `master.layouts` because the resource is not variant-owned.
- Kept physical layouts and navigation profiles as independent configuration dimensions.
- Aligned navigation reference columns with the canonical names `base_map_name`, `robot_map_name`, `route_profile_name` and `stitch_profile_name`.
- Replaced separate machine/code and machine/version constraints with `(machine_id, code, version)` uniqueness on each resource table.
- Added `uq_layout_published_version` and `uq_navigation_profile_published_version` partial unique indexes so a logical machine/code resource has at most one `PUBLISHED` version.
- Added `machine_id` to both variant-assignment tables and composite foreign keys to the variant and resource. Cross-machine assignments are rejected declaratively without triggers.
- Preserved the partial unique indexes that allow only one default layout and one default navigation profile per variant.
- Verified that the obsolete `CONSTRAINT uq_planning_group_part UNIQUE (product_group_id, part_id)` is absent from `master.part_planning_metrics`; only the two approved partial unique indexes remain.

## Customer-data decisions recorded for Phase 02

- `67115BN050-10-HAOS` is canonical. The single `67115BN050-10-HMTR` layout occurrence must be retained as a source discrepancy/data issue pending customer confirmation.
- Parts with missing lot/cycle inputs remain valid master data.
- Missing planning inputs and any derived metrics that cannot be calculated remain `NULL`.
- No lot, cycle, RDS identifier, map key, route key, stitch key or task label is guessed.

The currently identified incomplete-planning records include `715A2BN400-50` (SH-02 Variant 1), `715B2BN400-50` (SH-02 Variant 2), `90174-12022`, `90179-10234`, `90179-14075`, `72613-02210` and `57658-K0020`. Their source rows must be imported in Phase 02; only metrics that depend on missing lot/configuration inputs remain `NULL`.

No customer part rows are seeded in Phase 01, so these decisions are documented now and will be applied by the deterministic Phase 02 import.

## RDS boundary

`docs/references/rds/RdsApi_en.md` confirms public operations for WorkSite state and WindTask query/inspection/stop flows. The migration stores external identifiers and raw payloads without using them as application primary keys.

`integration.rds_worksites.navigation_profile_id` remains nullable. Whether warehouse/global RDS worksites are profile-independent is still an explicit domain decision; no constraint was tightened without that decision.

No task-creation endpoint, map-switch endpoint, route name, site ID or task label was inferred from the reference.

## Validation status

- Static review completed for table/FK creation order, named constraints, partial indexes and transaction boundaries.
- The migration is wrapped in `BEGIN`/`COMMIT`.
- Runtime validation completed against isolated, zero-state local PostgreSQL 18.3 clusters with `ON_ERROR_STOP=1`, including a fresh Phase 01.1 rerun.
- The migration completed successfully and created five application schemas, nine machines, 25 variants, four roles and three task types.
- The transactional smoke test completed successfully and rolled back its test data. It covers baseline seed counts, machine/variant uniqueness, both planning partial indexes, absence of variant resource ownership, shared resource IDs across two variants, one-default-per-variant enforcement, single-published-version enforcement, declarative cross-machine assignment rejection and slot-state consistency checks.
