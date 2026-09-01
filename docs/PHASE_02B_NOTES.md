# Phase 02B — Physical Layout Extraction

## Outcome

Phase 02B converts the five customer physical-layout workbooks into deterministic, source-traceable PDA display geometry. It seeds only `master.layouts`, `master.variant_layout_assignments`, and `master.layout_slots` through the existing Phase 01.1 schema.

No migration was changed. No robot coordinate, RDS WorkSite, waypoint, route, stitch, map, navigation profile, or drawing-scale-derived physical measurement was generated. Source-backed nominal container-master dimensions are propagated separately and are not robot geometry.

Generated artifacts:

- `db/seeds/generated/002b_customer_physical_layouts.sql`
- `data/generated/phase_02b/normalized_physical_layouts.json`
- `data/generated/phase_02b/layouts.csv`
- `data/generated/phase_02b/variant_layout_assignments.csv`
- `data/generated/phase_02b/layout_slots.csv`
- `data/generated/phase_02b/excluded_candidates.csv`
- `data/generated/phase_02b/data_issues.json`
- `data/generated/phase_02b/data_issues.csv`
- `data/generated/phase_02b/summary.json`
- `data/generated/phase_02b/source_manifest.json`
- one PNG per layout under `data/generated/phase_02b/review/`

## Workbook and sheet coverage

| Workbook | Sheet | Extracted machine region(s) |
|---|---|---|
| `PAH-01-A-412B.xlsx` | `1.Varyant` | PAH-01-A V1 `A1:CC76`; 412B V1 `A78:CC164` |
| `PAH-01-A-412B.xlsx` | `2. Varyant` | PAH-01-A V2 `A1:CC65`; 412B V2 `A67:CC164` |
| `PAH-01-A-412B.xlsx` | `3. Varyant` | 412B V3 `A78:CC164`; blank PAH template area excluded |
| `PAH-02-A-GAR.xlsx` | `1.Varyant` | PAH-02-A V1 `A1:CC65`; GAR V1 `A67:CC164` |
| `PAH-02-A-GAR.xlsx` | `2.Varyant` | PAH-02-A V2 `A1:CC76`; GAR V2 `A78:CC164` |
| `PAH-02-A-GAR.xlsx` | `3.Varyant` | GAR V3 `A78:CC164` |
| `SH-01-A HÜCRESİ VARYANT KUTU YERLEŞİMİ.xlsx` | five variant sheets | SH-01-A V1–V5, each constrained to `A1:CQ72` |
| `SH-01-B HÜCRESİ VARYANT KUTU YERLEŞİMİ.xlsx` | three variant sheets | SH-01-B V1–V3, each constrained to `A1:CQ72` |
| `SH-01-P1--SH-01-P2-SH-02-P1 -SH-02-P2.xlsx` | `1. VARYANT` | SH-01-P1/P2 `B1:CC138`; SEALER `CG1:FH138`; SH-02-P1/P2 `FI1:IG138` |

All 15 physical-layout sheets were rendered and visually checked before the normalized review images were accepted.

## Extraction and normalization rules

1. Only merged, bordered rectangles inside an explicitly verified machine canvas become logistics slots.
2. Explicit machine/hücre/robot rectangles become `MACHINE` role rows only where the workbook draws them. No missing machine rectangle is invented.
3. Slot codes are assigned deterministically in source reading order as `SLOT-001`, `SLOT-002`, and so on. Explicit machine regions use `MACHINE-001`.
4. Excel column widths and row heights are accumulated inside the verified source canvas and normalized independently to `ui_x`, `ui_y`, `ui_width`, and `ui_height` in `0.0–1.0`.
5. These fields are PDA display geometry only. Excel cell counts are not treated as metres, millimetres, robot coordinates, or SEER/RDS coordinates.
6. Part labels are normalized against the frozen Phase 02A canonical part set. Layout-only unknown parts are retained as evidence and never inserted into `master.parts`.
7. A known part seen outside the layout's assigned variant context remains a known part association, but the variant mismatch is reported. This preserves source evidence without changing the Phase 02A baseline.
8. Container precedence is explicit layout label first, then a unique Phase 02A planning mapping in the same machine/variant context. If that local value is blank, one globally consistent mapping for the exact same part code may be reused only when the slot has no variant mismatch. Multiple or absent candidates leave `default_container_type_id` `NULL`.
9. Category alone never supplies a physical footprint. A fully source-backed nominal container master dimension may populate slot footprint metadata; Excel drawing scale is never converted to millimetres.
10. Source workbook, sheet, cell range, and workbook SHA-256 are retained for every layout in normalized output and every DB slot in `metadata_json`.

## Shared layouts and SEALER conclusion

- SH-01-P1/P2 variants 1 and 2 share one physical layout.
- SH-02-P1/P2 variants 1 and 2 share one physical layout.
- The combined workbook explicitly labels the middle physical region `SEALER`. Its upper and lower slot evidence contains Phase 02A SEALER V2 and V1 parts respectively, so variants 1 and 2 share one SEALER layout.
- No SEALER V3 part or physical-layout evidence is present. SEALER V3 remains deliberately unassigned.

## Normalized record counts

| Metric | Count |
|---|---:|
| Customer physical-layout workbooks | 5 |
| Layouts | 21 |
| Variant-layout assignments | 24 |
| DB layout-slot rows | 183 |
| Logistics positions | 172 |
| Explicit machine-region rows | 11 |
| HIGH accepted slots | 133 |
| MEDIUM accepted slots | 50 |
| LOW accepted slots | 0 |
| LOW excluded candidates | 13 |
| Unresolved part-association slots | 28 |
| Unresolved container-association slots | 74 |
| Unknown physical footprints | 129 (118 logistics + 11 MACHINE) |
| Phase 02B data issues | 165 |

Confidence across all candidates is HIGH 133, MEDIUM 50, LOW 13. LOW candidates are not seeded.

### Runtime use of machine-region rows

The 183 DB rows deliberately contain two different semantic classes. The 172 logistics positions may participate in later logistics workflows. The 11 explicit machine/cell regions use `master.layout_slots.slot_role = 'MACHINE'` and exist only as physical/PDA display references.

Future workflow code must not use a `MACHINE` row as `ops.transport_requests.source_slot_id` or `destination_slot_id`, and must not place it in a source/destination slot group. The schema already carries the required discriminator in `slot_role`, so no migration was added for this rule. Because the current foreign keys do not filter by role, Phase 03 must enforce and test the invariant in domain/application validation.

## Layout and slot counts

Slot counts below include an explicit `MACHINE-001` row where the source draws a machine region.

| Machine | Layout | Assignments | DB slots | Logistics slots | Review image |
|---|---|---:|---:|---:|---|
| 412B | V1 | 1 | 8 | 8 | `review/412b-customer-physical-v1.png` |
| 412B | V2 | 1 | 9 | 9 | `review/412b-customer-physical-v2.png` |
| 412B | V3 | 1 | 8 | 8 | `review/412b-customer-physical-v3.png` |
| GAR | V1 | 1 | 9 | 9 | `review/gar-customer-physical-v1.png` |
| GAR | V2 | 1 | 8 | 8 | `review/gar-customer-physical-v2.png` |
| GAR | V3 | 1 | 8 | 8 | `review/gar-customer-physical-v3.png` |
| PAH-01-A | V1 | 1 | 7 | 7 | `review/pah-01-a-customer-physical-v1.png` |
| PAH-01-A | V2 | 1 | 6 | 6 | `review/pah-01-a-customer-physical-v2.png` |
| PAH-02-A | V1 | 1 | 6 | 6 | `review/pah-02-a-customer-physical-v1.png` |
| PAH-02-A | V2 | 1 | 7 | 7 | `review/pah-02-a-customer-physical-v2.png` |
| SEALER | shared V1/V2 | 2 | 15 | 14 | `review/sealer-customer-physical-shared-v1-v2.png` |
| SH-01-A | V1 | 1 | 10 | 9 | `review/sh-01-a-customer-physical-v1.png` |
| SH-01-A | V2 | 1 | 9 | 8 | `review/sh-01-a-customer-physical-v2.png` |
| SH-01-A | V3 | 1 | 9 | 8 | `review/sh-01-a-customer-physical-v3.png` |
| SH-01-A | V4 | 1 | 6 | 5 | `review/sh-01-a-customer-physical-v4.png` |
| SH-01-A | V5 | 1 | 7 | 6 | `review/sh-01-a-customer-physical-v5.png` |
| SH-01-B | V1 | 1 | 9 | 8 | `review/sh-01-b-customer-physical-v1.png` |
| SH-01-B | V2 | 1 | 9 | 8 | `review/sh-01-b-customer-physical-v2.png` |
| SH-01-B | V3 | 1 | 9 | 8 | `review/sh-01-b-customer-physical-v3.png` |
| SH-01-P1/P2 | shared V1/V2 | 2 | 15 | 14 | `review/sh-01-p1-sh-01-p2-customer-physical-shared-v1-v2.png` |
| SH-02-P1/P2 | shared V1/V2 | 2 | 9 | 8 | `review/sh-02-p1-sh-02-p2-customer-physical-shared-v1-v2.png` |

Review paths are relative to `data/generated/phase_02b/`.

## Unresolved part associations

The 28 unresolved part slots are explicit blank physical rectangles; no part value is guessed:

- 412B V2: `SLOT-009`
- GAR V1: `SLOT-007`–`SLOT-009`
- GAR V2: `SLOT-005`–`SLOT-008`
- GAR V3: `SLOT-001`–`SLOT-004`
- SEALER shared V1/V2: `SLOT-004`–`SLOT-008`, `SLOT-012`–`SLOT-014`
- SH-02-P1/P2 shared V1/V2: `SLOT-001`–`SLOT-008`

Twenty additional source part occurrences are canonical master parts but do not belong to the layout's assigned variant context. They remain visible as MEDIUM evidence and are reported as `SOURCE_PART_NOT_PLANNED_FOR_LAYOUT_VARIANT`. The largest group is the combined SH-01-P1/P2 layout, which uses BN400 labels while the Phase 02A planning baseline contains BN000.

## Unresolved container associations

Seventy-four logistics slots have no unique source-backed container mapping. Six SEALER slots were reconciled from one globally consistent exact-part mapping in SH-02 planning. The complete per-slot list is in `summary.json`, `data_issues.json`, and `data_issues.csv`. Affected layout groups are:

- 412B V2: `SLOT-009`
- GAR V1: `SLOT-007`–`SLOT-009`
- GAR V2: `SLOT-004`–`SLOT-008`
- GAR V3: `SLOT-001`–`SLOT-005`
- SEALER shared V1/V2: `SLOT-004`–`008`, `SLOT-012`–`014`
- SH-01-A V1: `SLOT-001`, `002`, `006`, `007`
- SH-01-A V2: `SLOT-001`, `002`, `005`, `006`
- SH-01-A V3: `SLOT-001`, `002`, `005`, `006`
- SH-01-A V4: `SLOT-004`, `005`
- SH-01-A V5: `SLOT-001`–`SLOT-004`
- SH-01-B V1: `SLOT-001`, `002`, `006`, `007`
- SH-01-B V2: `SLOT-001`, `004`, `005`, `007`
- SH-01-B V3: `SLOT-001`, `002`, `005`, `006`
- SH-01-P1/P2 shared V1/V2: all 14 logistics slots
- SH-02-P1/P2 shared V1/V2: all 8 logistics slots

No layout-only unknown container code was inserted or guessed.

## Container footprint issues

Dedicated customer container evidence resolves nominal footprint for 54 logistics slots: 16 `1284`, 20 `BYK`, 14 `ORT`, and 4 `KCK`. These are container-master dimensions only; they are not robot poses, approach clearances or docking geometry. The remaining 129 regions are unknown: 118 logistics and 11 explicit MACHINE regions.

| Container code | Slot count with NULL footprint |
|---|---:|
| 1285 | 2 |
| 285C | 4 |
| 4311 | 15 |
| 4322 | 15 |
| 4622 | 4 |
| ARABA | 4 |
| Unresolved container | 74 |

`KCK` is now assigned to four SEALER slots by exact-part evidence; two other reconciled SEALER slots use `4311`, whose global dimensions remain conflicting.

## Source regions not safely interpreted

The following LOW candidates are retained in the audit report but excluded from the seed:

- `PAH-01-A-412B.xlsx#3. Varyant`: blank `BR2:CC9`, `BR13:CC20`, `BR24:CC31`, `BR35:CC42`, `BR46:CC53`, `BR57:CC64`, `BR68:CC75`. PAH-01-A has no canonical V3.
- `SH-01-A ...xlsx#5.VARYANT!O74:V85`: blank merge outside the verified machine canvas.
- `SH-01-B ...xlsx#1. VARYANT!CW19:CY28`: narrow blank merge outside the verified machine canvas.
- Combined workbook `#1. VARYANT!L141:U141`, `L142:U142`, `L143:U143`, `L144:U144`: one-row footer/template merges.
- SEALER V3: no physical-layout or part evidence; no assignment generated.

## Data-issue report

| Issue code | Count |
|---|---:|
| `CONTAINER_ASSOCIATION_UNRESOLVED` | 74 |
| `CONTAINER_MAPPING_DERIVED_FROM_EXACT_PART` | 6 |
| `PART_ASSOCIATION_UNRESOLVED` | 28 |
| `SOURCE_PART_NOT_PLANNED_FOR_LAYOUT_VARIANT` | 20 |
| `PHYSICAL_FOOTPRINT_NOT_PROVIDED` | 21 |
| `LOW_CONFIDENCE_CANDIDATE_EXCLUDED` | 13 |
| `PART_CODE_SOURCE_DISCREPANCY` | 2 |
| `SEALER_VARIANT_3_LAYOUT_UNPROVEN` | 1 |
| **Total** | **165** |

The two HAOS/HMTR issues are the two SH-01-A V1 physical slots containing raw `67115BN050-10-HMTR`. Both retain that raw evidence and resolve to canonical `67115BN050-10-HAOS`.

The full unabridged report is `data/generated/phase_02b/data_issues.json` and `data/generated/phase_02b/data_issues.csv`.

## Validation results

- Unit/regression tests: 15/15 PASS.
- Pipeline output, generated SQL, CSV/JSON, and all 21 PNG review images are byte-equivalent across two independent runs.
- All five customer workbooks remained byte-identical before and after extraction.
- All layouts reference the known nine machines.
- Shared layout assignments work for SH-01-P1/P2, SH-02-P1/P2, and SEALER V1/V2.
- Slot codes are unique per layout; no normalized rectangles overlap unintentionally.
- All geometry is inside `0.0–1.0`.
- Every layout and slot has source provenance.
- Generated SQL contains no part/container master inserts and no RDS/navigation/route/map data.

## PostgreSQL 18.3 integration test

Application order:

1. `db/migrations/001_initial_schema.sql`
2. `db/seeds/generated/002a_customer_textual_master_data.sql`
3. `db/seeds/generated/002b_customer_physical_layouts.sql`
4. Run Phase 02B seed a second time.

Result: PASS.

- layouts: 21
- assignments: 24
- slots: 183
- cross-machine assignments: 0
- orphan slots: 0
- duplicate layouts: 0
- duplicate slots: 0
- out-of-bounds slots: 0
- missing provenance: 0
- forbidden coordinate keys: 0
- logical state digest before/after second import in this reconciliation validation run: `b16e7b47b640016d51f9eba83650e4c4`
- second import changed logical state: false

The digest includes generated identifiers and timestamps, so its value is only compared before/after the second seed inside the same fresh-cluster run. It is not a stable fingerprint across independently initialized clusters; generated SQL/JSON/CSV/PNG byte equivalence is validated separately by the pipeline tests.

The temporary PostgreSQL cluster was stopped and moved to the Windows Recycle Bin after the test.
