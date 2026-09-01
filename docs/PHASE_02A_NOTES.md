# TKG PDA Phase 02A — Textual Master Data Import Pipeline

**Status:** Completed; customer-source reconciliation applied without changing the original workbooks/images
**Date:** 2026-08-18
**Reconciliation date:** 2026-09-01
**Source digest:** `5e26bc18d3a34867c9ce211157b9af00de1c5e20753e85493591f759a527802f`

## Scope boundary

Phase 02A imports textual customer master/planning data only. It does not create `master.layouts`, `master.layout_slots`, PDA coordinates, container footprints, merged-cell geometry, route geometry or any other physical-layout configuration.

Layout workbooks are scanned only for textual part-code corroboration. Recorded Excel cell addresses are provenance locators, not physical coordinates. Cell colors, sizes, merged-area geometry, drawing shapes and placement coordinates are not extracted.

## Source files processed

Authoritative textual master/planning source:

- `2026- OPTİMAK -ROBOT AMR/AMR Varyant/AMR Çalışma Planı.xlsx`

Planning corroboration source:

- `2026- OPTİMAK -ROBOT AMR/AMR Takt time study.xlsx`

Interaction corroboration source:

- `2026- OPTİMAK -ROBOT AMR/AGV - BAKIM - PLC Çalışma.xlsx`

Textual corroboration only; no geometry:

- `2026- OPTİMAK -ROBOT AMR/AMR Varyant/PAH-01-A-412B.xlsx`
- `2026- OPTİMAK -ROBOT AMR/AMR Varyant/PAH-02-A-GAR.xlsx`
- `2026- OPTİMAK -ROBOT AMR/AMR Varyant/SH-01-A HÜCRESİ VARYANT KUTU YERLEŞİMİ.xlsx`
- `2026- OPTİMAK -ROBOT AMR/AMR Varyant/SH-01-B HÜCRESİ VARYANT KUTU YERLEŞİMİ.xlsx`
- `2026- OPTİMAK -ROBOT AMR/AMR Varyant/SH-01-P1--SH-01-P2-SH-02-P1 -SH-02-P2.xlsx`

Dedicated container-master visual evidence:

- `2026- OPTİMAK -ROBOT AMR/Hyundai STP/1284 ÇELİK KASA.png`
- `2026- OPTİMAK -ROBOT AMR/Hyundai STP/285C - Çelik kasa.jpg`
- `2026- OPTİMAK -ROBOT AMR/Hyundai STP/CLK Kasa tipleri.png`

Exact paths, sizes, SHA-256 hashes and roles are stored in `data/generated/phase_02a/source_manifest.json`. The pipeline hashes every source before and after extraction and aborts if a source changes during the run.

## Extraction and normalization rules

1. Machine identity comes from the authoritative work-plan sheet name and must match the Phase 01.1 baseline.
2. Variant identity comes from the column whose normalized header is `VARYANT`, not from visual cell-group numbering.
3. A populated `Parça` cell starts a product group; subsequent `Alt Parça` rows belong to that group until the next main part.
4. The same part remains valid across variants and across different product groups.
5. Part/container identifiers are Unicode-normalized, trimmed, hyphen-normalized and uppercased. Internal identifier spaces are removed.
6. Both alphanumeric part codes and customer numeric-hyphen codes such as `90174-12022` are accepted. Tiresiz salt numbers are not interpreted as part codes.
7. Numeric planning values are normalized to the database's three-decimal scale.
8. Blank inputs and Excel errors become `NULL`; no lot, container, movement or derived value is estimated.
9. Formula errors are recorded per source cell in the data-quality report.
10. Duplicate authoritative rows are merged by business key. If two nonblank values conflict, that field becomes `NULL` and a nonblocking issue is emitted.
11. `AMR Çalışma Planı.xlsx` wins over differing takt-study values. Every differing nonblank corroboration value is reported.
12. Raw nonblank container codes are preserved. Dedicated customer container images may resolve category and nominal type dimensions; conflicting row-level dimension evidence remains a data issue. Missing mappings stay `NULL`.
13. Only structural corruption, a missing required source/header or an impossible machine/variant reference stops generation. All current customer data issues are nonblocking.

## Precedence and known decisions

- `67115BN050-10-HAOS` is canonical.
- The two textual occurrences of `67115BN050-10-HMTR` in the SH-01-A Variant 1 layout sheet remain source evidence and generate discrepancy issues; they do not create a separate part.
- Missing-lot real parts remain in master data. Their lot and dependent AGV metrics remain `NULL`.
- `1284`, `BYK`, `ORT`, and `KCK` nominal dimensions and the 1284/285C container categories come from explicit customer sources. `ARABA` is category `CART`; its inconsistent row-level dimensions remain `NULL`.
- Physical layouts and navigation profiles remain outside this phase.

## Discovered normalized counts

| Record type | Count |
|---|---:|
| Source files | 11 (8 Excel + 3 customer images) |
| Unique parts | 161 |
| Container types | 10 |
| Product groups | 51 |
| Product-group related-part rows | 140 |
| Planning rows | 191 |
| Data issues | 159 |
| Excel formula-error cells | 33 |

These values were discovered from the first corrected deterministic extraction and are now regression-tested in `tests/phase_02a/expected_counts.json`.

## Data-quality results

All 159 issues are nonblocking: 156 `WARNING`, 3 `INFO`.

| Issue code | Count | Meaning |
|---|---:|---|
| `UNKNOWN_CONTAINER_MAPPING` | 60 | Raw container code is blank; DB mapping stays `NULL`. |
| `EXCEL_FORMULA_ERROR` | 33 | Cached Excel result is an error such as `#DIV/0!`; normalized metric stays `NULL`. |
| `SOURCE_VALUE_CONFLICT` | 25 | Work-plan and takt-study nonblank values differ; work-plan precedence retained. |
| `MISSING_PLANNING_INPUT` | 23 | Required source planning input such as Kutu Lotu is blank/invalid. |
| `AUTHORITATIVE_DUPLICATE_VALUE_CONFLICT` | 10 | Duplicate work-plan business context has differing values; conflicting field set to `NULL`. |
| `CONTAINER_DIMENSION_SOURCE_CONFLICT` | 3 | Dedicated BYK/ORT/KCK size chart differs from row-level planning dimensions; both are retained with explicit precedence. |
| `CONTAINER_CATEGORY_SOURCE_RECONCILED` | 2 | Dedicated/literal evidence resolves weaker or conflicting row hints. |
| `CONTAINER_CATEGORY_UNRESOLVED` | 1 | Only `1285` remains category `OTHER`. |
| `PART_CODE_SOURCE_DISCREPANCY` | 2 | HMTR layout evidence normalized to canonical HAOS. |

Unresolved container mapping/category values are now only:

- `(blank)` — no raw code exists; mapping is `NULL`.
- `1285` — raw code exists, but category and nominal dimensions are absent.

Reconciled type master: `1284 = 1200×800×400`, `BYK = 1200×750×600`, `ORT = 950×650×550`, `KCK = 800×550×450`; `285C` is `CONTAINER`; `ARABA` is `CART`.

The complete issue set is in both `data_issues.json` and `data_issues.csv`; no issue is omitted from those artifacts.

Known customer-approved incomplete planning parts produce nine DB planning contexts because two codes occur under two product groups:

- `715A2BN400-50` — one context.
- `715B2BN400-50` — one context.
- `90174-12022` — two contexts.
- `90179-10234` — one context.
- `90179-14075` — two contexts.
- `72613-02210` — one context.
- `57658-K0020` — one context.

## Generated outputs

- `data/generated/phase_02a/normalized_customer_data.json`
- `data/generated/phase_02a/source_manifest.json`
- `data/generated/phase_02a/summary.json`
- `data/generated/phase_02a/parts.csv`
- `data/generated/phase_02a/container_types.csv`
- `data/generated/phase_02a/product_groups.csv`
- `data/generated/phase_02a/product_group_parts.csv`
- `data/generated/phase_02a/planning_metrics.csv`
- `data/generated/phase_02a/data_issues.json`
- `data/generated/phase_02a/data_issues.csv`
- `db/seeds/generated/002a_customer_textual_master_data.sql`

The generated SQL is deterministic and idempotent. It resolves baseline machine/variant UUIDs by stable customer codes and never embeds generated UUID values.

## Automated validation

`tests/phase_02a/test_pipeline.py` contains eleven deterministic/unit/regression tests covering:

- normalization,
- byte-equivalent reruns,
- blank-to-`NULL`,
- formula-error-to-issue/`NULL`,
- cross-variant part reuse,
- cross-product-group part reuse,
- HAOS/HMTR discrepancy,
- unknown container handling,
- source immutability,
- discovered-count regression.
- customer container category/dimension reconciliation.

Result: **11/11 PASS**.

## PostgreSQL integration result

`tests/phase_02a/run_postgres_integration.ps1` created a fresh isolated PostgreSQL 18.3 cluster, then ran:

1. `db/migrations/001_initial_schema.sql`
2. `db/seeds/generated/002a_customer_textual_master_data.sql`
3. the same generated seed a second time

Result: **PASS**.

- First seed: 161 parts, 10 container types, 51 product groups, 140 group members and 191 planning rows inserted.
- Second seed: zero inserts/updates; counts remained identical.
- Orphan product groups: 0.
- Orphan planning rows: 0.
- Known incomplete planning contexts retained with `NULL` lot/AGV metrics: 9.

The temporary cluster was stopped and moved to the Windows Recycle Bin after the test.

## Historical phase boundary

Phase 02A originally stopped before colored layout parsing. Phase 02B was later completed as a separate safety-sensitive pipeline; the customer-source reconciliation only enriches container master evidence and does not merge robot/layout geometry into Phase 02A.
