# Customer Reference Package

The original customer source package remains in the repository at:

```text
2026- OPTİMAK -ROBOT AMR/
```

Primary database/import references:

- `AGV - BAKIM - PLC Çalışma.xlsx` — operator interaction and container-action model.
- `AMR Takt time study.xlsx` — part, lot, cycle, volume and AGV movement planning source.
- `AMR Varyant/AMR Çalışma Planı.xlsx` — machine/variant planning and capacity summary.
- `AMR Varyant/*VARYANT KUTU YERLEŞİMİ.xlsx` and combined layout workbooks — physical slot geometry and variant/layout evidence.
- `Hyundai STP/` images and STEP assemblies — physical container/fixture references.
- `OPTİMAK- Lay out_rev1.dwg` — factory layout reference.

## Import rules already decided

- Use `67115BN050-10-HAOS` as the canonical code; retain the isolated `HMTR` occurrence as a source discrepancy.
- Do not remove real parts because planning inputs are incomplete.
- Leave missing source inputs and uncomputable derived metrics as `NULL`.
- Do not invent container mappings, physical slots, RDS WorkSite IDs or navigation identifiers.
- Reusable physical layouts/navigation profiles may be assigned to more than one variant.
- Dedicated `Hyundai STP` container images are authoritative for the explicitly labelled nominal container categories/dimensions recorded in Phase 02A; they are not robot geometry.

The complete cross-source A/B/C decision is recorded in [CUSTOMER_SOURCE_RECONCILIATION.md](../../commissioning/CUSTOMER_SOURCE_RECONCILIATION.md). After reconciliation, only `1285` remains a nonblank container code with unresolved category; blank planning mappings remain `NULL`.

Known incomplete-planning part rows that must not be dropped are `715A2BN400-50`, `715B2BN400-50`, `90174-12022`, `90179-10234`, `90179-14075`, `72613-02210` and `57658-K0020`.

The Phase 02 importer must read these files deterministically and record source provenance/data issues rather than silently normalizing uncertain values.
