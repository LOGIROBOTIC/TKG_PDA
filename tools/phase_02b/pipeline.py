from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import textwrap
import unicodedata
from collections import Counter, defaultdict
from dataclasses import dataclass
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path
from typing import Any, Iterable, Sequence

from openpyxl import load_workbook
from openpyxl.utils import column_index_from_string, get_column_letter, range_boundaries
from PIL import Image, ImageDraw, ImageFont


SCHEMA_VERSION = "phase_02b_v2"
SOURCE_ROOT = Path("2026- OPTİMAK -ROBOT AMR") / "AMR Varyant"
PHASE_02A_DATA = Path("data/generated/phase_02a/normalized_customer_data.json")

PAH_01_412B = SOURCE_ROOT / "PAH-01-A-412B.xlsx"
PAH_02_GAR = SOURCE_ROOT / "PAH-02-A-GAR.xlsx"
SH_01_A = SOURCE_ROOT / "SH-01-A HÜCRESİ VARYANT KUTU YERLEŞİMİ.xlsx"
SH_01_B = SOURCE_ROOT / "SH-01-B HÜCRESİ VARYANT KUTU YERLEŞİMİ.xlsx"
SHARED = SOURCE_ROOT / "SH-01-P1--SH-01-P2-SH-02-P1 -SH-02-P2.xlsx"
SOURCE_FILES = (PAH_01_412B, PAH_02_GAR, SH_01_A, SH_01_B, SHARED)

KNOWN_MACHINES: dict[str, tuple[str, ...]] = {
    "SH-01-A": ("1", "2", "3", "4", "5"),
    "SH-01-B": ("1", "2", "3"),
    "SH-01-P1--SH-01-P2": ("1", "2"),
    "SH-02-P1 -SH-02-P2": ("1", "2"),
    "SEALER": ("1", "2", "3"),
    "PAH-01-A": ("1", "2"),
    "412B": ("1", "2", "3"),
    "PAH-02-A": ("1", "2"),
    "GAR": ("1", "2", "3"),
}

CANONICAL_PART_ALIASES = {"67115BN050-10-HMTR": "67115BN050-10-HAOS"}
PART_CODE_RE = re.compile(r"^(?=.*\d)[A-Z0-9]{5,}(?:-[A-Z0-9]+)*$")
DIMENSION_RE = re.compile(r"(?<!\d)(\d{3,4})\s*[Xx/]\s*(\d{3,4})(?!\d)")
GEOMETRY_QUANTUM = Decimal("0.0001")
TURKISH_ASCII = str.maketrans(
    {
        "ı": "i", "İ": "I", "ş": "s", "Ş": "S", "ğ": "g", "Ğ": "G",
        "ç": "c", "Ç": "C", "ö": "o", "Ö": "O", "ü": "u", "Ü": "U",
    }
)


class StructuralExtractionError(RuntimeError):
    """Raised only for corruption or references that cannot be represented safely."""


@dataclass(frozen=True)
class LayoutSpec:
    machine_code: str
    code: str
    name: str
    workbook: Path
    sheet: str
    canvas_range: str
    variant_codes: tuple[str, ...]
    candidate_ranges: tuple[str, ...]
    machine_range: str | None = None
    source_decision: str = "explicit_workbook_variant"


def merged_ranges_in_rows(sheet: Any, min_row: int, max_row: int) -> tuple[str, ...]:
    return tuple(
        str(item)
        for item in sorted(
            sheet.merged_cells.ranges,
            key=lambda value: (value.min_row, value.min_col, value.max_row, value.max_col),
        )
        if item.min_row >= min_row and item.max_row <= max_row
    )


def normalize_text(value: Any) -> str | None:
    if value is None:
        return None
    text = unicodedata.normalize("NFKC", str(value))
    text = text.replace("–", "-").replace("—", "-").replace("−", "-")
    text = re.sub(r"\s+", " ", text).strip()
    return text or None


def normalize_label(value: Any) -> str:
    text = normalize_text(value) or ""
    text = unicodedata.normalize("NFKD", text.translate(TURKISH_ASCII))
    text = "".join(char for char in text if not unicodedata.combining(char))
    return re.sub(r"\s+", " ", re.sub(r"[^A-Za-z0-9]+", " ", text).strip().upper())


def normalize_identifier(value: Any) -> str | None:
    text = normalize_text(value)
    return None if text is None else text.upper().replace(" ", "")


def slug(value: str) -> str:
    normalized = unicodedata.normalize("NFKD", value.translate(TURKISH_ASCII))
    ascii_value = "".join(char for char in normalized if not unicodedata.combining(char))
    return re.sub(r"[^a-z0-9]+", "-", ascii_value.lower()).strip("-")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def manifest_digest(manifest: Sequence[dict[str, Any]]) -> str:
    payload = json.dumps(manifest, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def build_manifest(project_root: Path) -> list[dict[str, Any]]:
    manifest: list[dict[str, Any]] = []
    for relative in SOURCE_FILES:
        path = project_root / relative
        if not path.is_file():
            raise StructuralExtractionError(f"Required physical-layout workbook is missing: {relative.as_posix()}")
        manifest.append(
            {
                "path": relative.as_posix(),
                "size_bytes": path.stat().st_size,
                "sha256": sha256_file(path),
                "role": "authoritative_physical_layout_geometry",
            }
        )
    phase_02a_path = project_root / PHASE_02A_DATA
    if not phase_02a_path.is_file():
        raise StructuralExtractionError(f"Phase 02A baseline is missing: {PHASE_02A_DATA.as_posix()}")
    manifest.append(
        {
            "path": PHASE_02A_DATA.as_posix(),
            "size_bytes": phase_02a_path.stat().st_size,
            "sha256": sha256_file(phase_02a_path),
            "role": "frozen_textual_master_reference",
        }
    )
    return sorted(manifest, key=lambda item: item["path"].casefold())


def make_issue_id(payload: dict[str, Any]) -> str:
    canonical = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()[:20]


def add_issue(
    issues: dict[str, dict[str, Any]],
    code: str,
    severity: str,
    message: str,
    **context: Any,
) -> None:
    payload = {"code": code, "severity": severity, "message": message, **context}
    issue_id = make_issue_id(payload)
    issues[issue_id] = {"issue_id": issue_id, "blocking": False, **payload}


def range_payload(project_root: Path, workbook: Path, sheet: str, cell_range: str) -> dict[str, Any]:
    return {
        "file": workbook.as_posix(),
        "sheet": sheet,
        "range": cell_range,
        "workbook_sha256": sha256_file(project_root / workbook),
    }


def parse_range(cell_range: str) -> tuple[int, int, int, int]:
    min_col, min_row, max_col, max_row = range_boundaries(cell_range)
    return min_col, min_row, max_col, max_row


def contains_range(outer: str, inner: str) -> bool:
    o_min_col, o_min_row, o_max_col, o_max_row = parse_range(outer)
    i_min_col, i_min_row, i_max_col, i_max_row = parse_range(inner)
    return (
        o_min_col <= i_min_col <= i_max_col <= o_max_col
        and o_min_row <= i_min_row <= i_max_row <= o_max_row
    )


def source_value(sheet: Any, cell_range: str) -> Any:
    min_col, min_row, _, _ = parse_range(cell_range)
    return sheet.cell(min_row, min_col).value


def merged_has_outer_border(sheet: Any, cell_range: str) -> bool:
    min_col, min_row, max_col, max_row = parse_range(cell_range)
    checks = (
        sheet.cell(min_row, min_col).border.left.style,
        sheet.cell(min_row, min_col).border.top.style,
        sheet.cell(min_row, max_col).border.right.style,
        sheet.cell(max_row, min_col).border.bottom.style,
        sheet.cell(max_row, max_col).border.right.style,
        sheet.cell(max_row, max_col).border.bottom.style,
    )
    return any(style is not None for style in checks)


def column_width(sheet: Any, column: int) -> float:
    letter = get_column_letter(column)
    explicit = sheet.column_dimensions[letter].width
    if explicit is not None:
        return float(explicit)
    if sheet.sheet_format.defaultColWidth is not None:
        return float(sheet.sheet_format.defaultColWidth)
    return 13.0


def row_height(sheet: Any, row: int) -> float:
    explicit = sheet.row_dimensions[row].height
    if explicit is not None:
        return float(explicit)
    if sheet.sheet_format.defaultRowHeight is not None:
        return float(sheet.sheet_format.defaultRowHeight)
    return 15.0


def quantized(value: float) -> float:
    return float(Decimal(str(value)).quantize(GEOMETRY_QUANTUM, rounding=ROUND_HALF_UP))


def normalized_geometry(sheet: Any, canvas_range: str, cell_range: str) -> dict[str, float]:
    c_min_col, c_min_row, c_max_col, c_max_row = parse_range(canvas_range)
    s_min_col, s_min_row, s_max_col, s_max_row = parse_range(cell_range)
    if not contains_range(canvas_range, cell_range):
        raise StructuralExtractionError(f"Slot {cell_range} is outside canvas {canvas_range} on {sheet.title}")
    column_sizes = {column: column_width(sheet, column) for column in range(c_min_col, c_max_col + 1)}
    row_sizes = {row: row_height(sheet, row) for row in range(c_min_row, c_max_row + 1)}
    total_width = sum(column_sizes.values())
    total_height = sum(row_sizes.values())
    x = sum(column_sizes[column] for column in range(c_min_col, s_min_col)) / total_width
    y = sum(row_sizes[row] for row in range(c_min_row, s_min_row)) / total_height
    width = sum(column_sizes[column] for column in range(s_min_col, s_max_col + 1)) / total_width
    height = sum(row_sizes[row] for row in range(s_min_row, s_max_row + 1)) / total_height
    result = {
        "ui_x": quantized(x),
        "ui_y": quantized(y),
        "ui_width": quantized(width),
        "ui_height": quantized(height),
    }
    if any(value < 0.0 or value > 1.0 for value in result.values()):
        raise StructuralExtractionError(f"Normalized geometry escaped 0..1 for {sheet.title}!{cell_range}")
    if result["ui_x"] + result["ui_width"] > 1.0001 or result["ui_y"] + result["ui_height"] > 1.0001:
        raise StructuralExtractionError(f"Normalized rectangle escaped its canvas for {sheet.title}!{cell_range}")
    return result


def ranges_inside(sheet: Any, canvas_range: str, excluded: Iterable[str] = ()) -> tuple[str, ...]:
    excluded_set = set(excluded)
    return tuple(
        str(item)
        for item in sorted(
            sheet.merged_cells.ranges,
            key=lambda value: (value.min_row, value.min_col, value.max_row, value.max_col),
        )
        if str(item) not in excluded_set and contains_range(canvas_range, str(item))
    )


def require_sheet(workbook: Any, workbook_path: Path, sheet_name: str) -> Any:
    if sheet_name not in workbook.sheetnames:
        raise StructuralExtractionError(f"Required sheet is missing: {workbook_path.as_posix()}#{sheet_name}")
    return workbook[sheet_name]


def build_layout_specs(project_root: Path) -> tuple[list[LayoutSpec], list[dict[str, Any]]]:
    books = {
        relative: load_workbook(project_root / relative, data_only=False, read_only=False)
        for relative in SOURCE_FILES
    }
    specs: list[LayoutSpec] = []
    try:
        def add(
            machine: str,
            code: str,
            name: str,
            book: Path,
            sheet_name: str,
            canvas: str,
            variants: tuple[str, ...],
            machine_range: str | None = None,
            decision: str = "explicit_workbook_variant",
        ) -> None:
            sheet = require_sheet(books[book], book, sheet_name)
            candidates = ranges_inside(sheet, canvas, (machine_range,) if machine_range else ())
            specs.append(
                LayoutSpec(
                    machine_code=machine,
                    code=code,
                    name=name,
                    workbook=book,
                    sheet=sheet_name,
                    canvas_range=canvas,
                    variant_codes=variants,
                    candidate_ranges=candidates,
                    machine_range=machine_range,
                    source_decision=decision,
                )
            )

        add("PAH-01-A", "CUSTOMER-PHYSICAL-V1", "PAH-01-A / V1", PAH_01_412B, "1.Varyant", "A1:CC76", ("1",))
        add("412B", "CUSTOMER-PHYSICAL-V1", "412B / V1", PAH_01_412B, "1.Varyant", "A78:CC164", ("1",))
        add("PAH-01-A", "CUSTOMER-PHYSICAL-V2", "PAH-01-A / V2", PAH_01_412B, "2. Varyant", "A1:CC65", ("2",))
        add("412B", "CUSTOMER-PHYSICAL-V2", "412B / V2", PAH_01_412B, "2. Varyant", "A67:CC164", ("2",))
        add("412B", "CUSTOMER-PHYSICAL-V3", "412B / V3", PAH_01_412B, "3. Varyant", "A78:CC164", ("3",))

        add("PAH-02-A", "CUSTOMER-PHYSICAL-V1", "PAH-02-A / V1", PAH_02_GAR, "1.Varyant", "A1:CC65", ("1",))
        add("GAR", "CUSTOMER-PHYSICAL-V1", "GAR / V1", PAH_02_GAR, "1.Varyant", "A67:CC164", ("1",))
        add("PAH-02-A", "CUSTOMER-PHYSICAL-V2", "PAH-02-A / V2", PAH_02_GAR, "2.Varyant", "A1:CC76", ("2",))
        add("GAR", "CUSTOMER-PHYSICAL-V2", "GAR / V2", PAH_02_GAR, "2.Varyant", "A78:CC164", ("2",))
        add("GAR", "CUSTOMER-PHYSICAL-V3", "GAR / V3", PAH_02_GAR, "3.Varyant", "A78:CC164", ("3",))

        sh01a_sheets = ("1. VARYANT", "2.VARYANT", "3.VARYANT", "4. VARYANT", "5.VARYANT")
        for variant, sheet_name in enumerate(sh01a_sheets, start=1):
            add(
                "SH-01-A", f"CUSTOMER-PHYSICAL-V{variant}", f"SH-01-A / V{variant}",
                SH_01_A, sheet_name, "A1:CQ72", (str(variant),), "Q2:CQ56",
            )

        sh01b_sheets = ("1. VARYANT", "2. VARYANT", "3. VARYANT")
        for variant, sheet_name in enumerate(sh01b_sheets, start=1):
            add(
                "SH-01-B", f"CUSTOMER-PHYSICAL-V{variant}", f"SH-01-B / V{variant}",
                SH_01_B, sheet_name, "A1:CQ72", (str(variant),), "B2:CB56",
            )

        add(
            "SH-01-P1--SH-01-P2", "CUSTOMER-PHYSICAL-SHARED-V1-V2",
            "SH-01-P1 / SH-01-P2 shared", SHARED, "1. VARYANT", "B1:CC138", ("1", "2"),
            "B29:CB113", "shared_layout_explicit_in_single_customer_sheet",
        )
        add(
            "SEALER", "CUSTOMER-PHYSICAL-SHARED-V1-V2", "SEALER shared / V1-V2",
            SHARED, "1. VARYANT", "CG1:FH138", ("1", "2"), "CK26:EZ113",
            "sealer_region_and_v1_v2_part_evidence_in_combined_sheet",
        )
        add(
            "SH-02-P1 -SH-02-P2", "CUSTOMER-PHYSICAL-SHARED-V1-V2",
            "SH-02-P1 / SH-02-P2 shared", SHARED, "1. VARYANT", "FI1:IG138", ("1", "2"),
            "FI29:IG113", "shared_layout_explicit_in_single_customer_sheet",
        )

        consumed: set[tuple[Path, str, str]] = set()
        for spec in specs:
            consumed.update((spec.workbook, spec.sheet, item) for item in spec.candidate_ranges)
            if spec.machine_range:
                consumed.add((spec.workbook, spec.sheet, spec.machine_range))

        excluded: list[dict[str, Any]] = []
        for book_path, workbook in books.items():
            for sheet in workbook.worksheets:
                for merged in sorted(
                    sheet.merged_cells.ranges,
                    key=lambda value: (value.min_row, value.min_col, value.max_row, value.max_col),
                ):
                    cell_range = str(merged)
                    if (book_path, sheet.title, cell_range) in consumed:
                        continue
                    reason = (
                        "PAH-01-A has no canonical variant 3; blank template rectangles are not a layout."
                        if book_path == PAH_01_412B and sheet.title == "3. Varyant" and merged.max_row <= 76
                        else "Merged template artifact is outside every proven machine extraction region."
                    )
                    excluded.append(
                        {
                            "confidence": "LOW",
                            "status": "EXCLUDED",
                            "reason": reason,
                            "source": range_payload(project_root, book_path, sheet.title, cell_range),
                            "raw_label": normalize_text(source_value(sheet, cell_range)),
                            "has_outer_border": merged_has_outer_border(sheet, cell_range),
                        }
                    )
        return sorted(specs, key=lambda item: (item.machine_code, item.code)), excluded
    finally:
        for workbook in books.values():
            workbook.close()


def label_evidence(value: Any, known_containers: set[str]) -> dict[str, Any]:
    raw_label = None if value is None else str(value)
    raw_parts: list[str] = []
    explicit_containers: list[str] = []
    unknown_container_tokens: list[str] = []
    ignored_tokens: list[str] = []
    for raw_token in re.split(r"[\r\n]+", raw_label or ""):
        token = normalize_identifier(raw_token)
        token_label = normalize_label(raw_token)
        if not token:
            continue
        container_match = None
        for container_code in sorted(known_containers, key=lambda item: (-len(item), item)):
            container_label = normalize_label(container_code)
            if token_label == container_label:
                container_match = container_code
                break
            if token_label.startswith(container_label + " ") and any(
                marker in token_label for marker in ("MIX", "PALET", "KASA", "ARABA", "KUTU")
            ):
                container_match = container_code
                break
        if container_match:
            explicit_containers.append(container_match)
            continue
        if any(marker in token_label for marker in ("MIX", "PALET")):
            possible_code = token_label.split(" ", 1)[0]
            if possible_code and any(char.isdigit() for char in possible_code):
                unknown_container_tokens.append(possible_code)
            else:
                ignored_tokens.append(raw_token)
            continue
        if any(marker in token_label for marker in ("MAMUL", "KASA")):
            ignored_tokens.append(raw_token)
            continue
        if PART_CODE_RE.fullmatch(token) and ("-" in token or any(char.isalpha() for char in token)):
            raw_parts.append(token)
        else:
            ignored_tokens.append(raw_token)

    dimensions = [
        {"width_mm": int(match.group(1)), "height_mm": int(match.group(2))}
        for match in DIMENSION_RE.finditer(raw_label or "")
    ]
    return {
        "raw_label": raw_label,
        "raw_part_codes": sorted(set(raw_parts)),
        "explicit_container_codes": sorted(set(explicit_containers)),
        "unknown_container_tokens": sorted(set(unknown_container_tokens)),
        "ignored_label_tokens": sorted(set(filter(None, (normalize_text(item) for item in ignored_tokens)))),
        "explicit_dimensions_mm": dimensions,
    }


def phase_02a_context(project_root: Path) -> dict[str, Any]:
    payload = json.loads((project_root / PHASE_02A_DATA).read_text(encoding="utf-8"))
    return {
        "payload": payload,
        "parts": {item["part_code"] for item in payload["parts"]},
        "containers": {item["code"] for item in payload["container_types"]},
        "container_records": {item["code"]: item for item in payload["container_types"]},
        "planning": payload["planning_metrics"],
    }


def layout_key(machine_code: str, layout_code: str) -> str:
    return f"{machine_code}::{layout_code}"


def source_location(source: dict[str, Any]) -> str:
    return f"{source['file']}#{source['sheet']}!{source['range']}"


def derive_container(
    machine_code: str,
    variant_codes: tuple[str, ...],
    resolved_parts: list[str],
    explicit_containers: list[str],
    planning: list[dict[str, Any]],
    allow_cross_context: bool,
) -> dict[str, Any]:
    evidence_rows = [
        row for row in planning
        if row["machine_code"] == machine_code
        and row["variant_code"] in variant_codes
        and row["part_code"] in resolved_parts
    ]
    planning_codes = sorted({row["container_code"] for row in evidence_rows if row["container_code"]})
    refs_by_key = {
        json.dumps(ref, ensure_ascii=False, sort_keys=True): ref
        for row in evidence_rows for ref in row.get("source_refs", [])
    }
    if len(explicit_containers) == 1:
        return {
            "container_code": explicit_containers[0],
            "evidence": "layout_label",
            "planning_container_candidates": planning_codes,
            "planning_source_refs": [refs_by_key[key] for key in sorted(refs_by_key)],
            "ambiguous": False,
        }
    if len(explicit_containers) > 1:
        return {
            "container_code": None,
            "evidence": None,
            "planning_container_candidates": planning_codes,
            "planning_source_refs": [],
            "ambiguous": True,
        }
    if len(planning_codes) == 1:
        return {
            "container_code": planning_codes[0],
            "evidence": "phase_02a_planning",
            "planning_container_candidates": planning_codes,
            "planning_source_refs": [
                refs_by_key[key]
                for key in sorted(
                    {
                        json.dumps(ref, ensure_ascii=False, sort_keys=True)
                        for row in evidence_rows for ref in row.get("source_refs", [])
                        if row.get("container_code") == planning_codes[0]
                    }
                )
            ],
            "ambiguous": False,
        }

    cross_context_rows = [
        row for row in planning
        if allow_cross_context
        and row["part_code"] in resolved_parts
        and row["container_code"]
    ]
    cross_context_codes = sorted({row["container_code"] for row in cross_context_rows})
    if not planning_codes and len(cross_context_codes) == 1:
        selected = cross_context_codes[0]
        cross_context_refs = {
            json.dumps(ref, ensure_ascii=False, sort_keys=True): ref
            for row in cross_context_rows
            if row["container_code"] == selected
            for ref in row.get("source_refs", [])
        }
        return {
            "container_code": selected,
            "evidence": "phase_02a_exact_part_cross_context",
            "planning_container_candidates": cross_context_codes,
            "planning_source_refs": [cross_context_refs[key] for key in sorted(cross_context_refs)],
            "ambiguous": False,
        }
    return {
        "container_code": None,
        "evidence": None,
        "planning_container_candidates": planning_codes or cross_context_codes,
        "planning_source_refs": [],
        "ambiguous": len(planning_codes or cross_context_codes) > 1,
    }


def rectangles_overlap(left: dict[str, Any], right: dict[str, Any]) -> bool:
    horizontal = min(
        left["ui_x"] + left["ui_width"], right["ui_x"] + right["ui_width"]
    ) - max(left["ui_x"], right["ui_x"])
    vertical = min(
        left["ui_y"] + left["ui_height"], right["ui_y"] + right["ui_height"]
    ) - max(left["ui_y"], right["ui_y"])
    return horizontal > 0.0001 and vertical > 0.0001


def validate_layout_geometry(layout: dict[str, Any], slots: list[dict[str, Any]]) -> None:
    codes = [slot["code"] for slot in slots]
    if len(codes) != len(set(codes)):
        raise StructuralExtractionError(f"Duplicate slot code in {layout['layout_key']}")
    for slot in slots:
        for key in ("ui_x", "ui_y", "ui_width", "ui_height"):
            if slot[key] < 0.0 or slot[key] > 1.0:
                raise StructuralExtractionError(f"Geometry outside 0..1 in {layout['layout_key']}::{slot['code']}")
        if slot["ui_x"] + slot["ui_width"] > 1.0001 or slot["ui_y"] + slot["ui_height"] > 1.0001:
            raise StructuralExtractionError(f"Slot escapes canvas in {layout['layout_key']}::{slot['code']}")
    for index, left in enumerate(slots):
        for right in slots[index + 1:]:
            if rectangles_overlap(left, right):
                raise StructuralExtractionError(
                    f"Unintended overlap in {layout['layout_key']}: {left['code']} / {right['code']}"
                )


def build_dataset(project_root: Path) -> dict[str, Any]:
    project_root = project_root.resolve()
    manifest_before = build_manifest(project_root)
    specs, excluded_candidates = build_layout_specs(project_root)
    phase02a = phase_02a_context(project_root)
    authoritative_parts: set[str] = phase02a["parts"]
    known_containers: set[str] = phase02a["containers"]
    container_records: dict[str, dict[str, Any]] = phase02a["container_records"]
    planning: list[dict[str, Any]] = phase02a["planning"]
    issues: dict[str, dict[str, Any]] = {}

    layouts: list[dict[str, Any]] = []
    assignments: list[dict[str, Any]] = []
    slots: list[dict[str, Any]] = []
    workbooks: dict[Path, Any] = {}
    try:
        for spec in specs:
            if spec.machine_code not in KNOWN_MACHINES:
                raise StructuralExtractionError(f"Unknown machine reference: {spec.machine_code}")
            invalid_variants = sorted(set(spec.variant_codes) - set(KNOWN_MACHINES[spec.machine_code]))
            if invalid_variants:
                raise StructuralExtractionError(
                    f"Invalid variants for {spec.machine_code}: {', '.join(invalid_variants)}"
                )
            if spec.workbook not in workbooks:
                workbooks[spec.workbook] = load_workbook(
                    project_root / spec.workbook, data_only=False, read_only=False
                )
            sheet = require_sheet(workbooks[spec.workbook], spec.workbook, spec.sheet)
            key = layout_key(spec.machine_code, spec.code)
            source = range_payload(project_root, spec.workbook, spec.sheet, spec.canvas_range)
            review_image = (
                Path("data/generated/phase_02b/review")
                / f"{slug(spec.machine_code)}-{slug(spec.code)}.png"
            ).as_posix()
            layout = {
                "layout_key": key,
                "machine_code": spec.machine_code,
                "code": spec.code,
                "name": spec.name,
                "version": 1,
                "status": "PUBLISHED",
                "variant_codes": list(spec.variant_codes),
                "source": source,
                "source_decision": spec.source_decision,
                "review_image": review_image,
            }
            layouts.append(layout)
            for variant_code in spec.variant_codes:
                assignments.append(
                    {
                        "layout_key": key,
                        "machine_code": spec.machine_code,
                        "variant_code": variant_code,
                        "layout_code": spec.code,
                        "is_default": True,
                        "source": source,
                    }
                )

            layout_slots: list[dict[str, Any]] = []
            if spec.machine_range:
                machine_source = range_payload(
                    project_root, spec.workbook, spec.sheet, spec.machine_range
                )
                machine_slot = {
                    "layout_key": key,
                    "machine_code": spec.machine_code,
                    "layout_code": spec.code,
                    "code": "MACHINE-001",
                    "display_name": spec.machine_code,
                    "slot_role": "MACHINE",
                    "default_container_code": None,
                    "display_order": 0,
                    "allows_empty_load": False,
                    "allows_full_load": False,
                    "is_enabled": True,
                    "confidence": "HIGH",
                    "part_association_unresolved": False,
                    "container_association_unresolved": False,
                    "physical_footprint_unknown": True,
                    **normalized_geometry(sheet, spec.canvas_range, spec.machine_range),
                    "metadata": {
                        "managed_by": "phase_02b",
                        "schema_version": SCHEMA_VERSION,
                        "confidence": "HIGH",
                        "source": machine_source,
                        "source_kind": "machine_region",
                        "raw_label": normalize_text(source_value(sheet, spec.machine_range)),
                        "raw_part_codes": [],
                        "resolved_part_codes": [],
                        "unresolved_part_codes": [],
                        "container_code": None,
                        "container_evidence": None,
                        "physical_footprint_mm": None,
                        "geometry_basis": "excel_cell_display_grid",
                    },
                }
                layout_slots.append(machine_slot)

            planned_parts = {
                row["part_code"]
                for row in planning
                if row["machine_code"] == spec.machine_code
                and row["variant_code"] in spec.variant_codes
            }
            ordered_candidates = sorted(
                spec.candidate_ranges,
                key=lambda item: (parse_range(item)[1], parse_range(item)[0], item),
            )
            for ordinal, cell_range in enumerate(ordered_candidates, start=1):
                evidence = label_evidence(source_value(sheet, cell_range), known_containers)
                source = range_payload(project_root, spec.workbook, spec.sheet, cell_range)
                canonical_parts: list[str] = []
                resolved_parts: list[str] = []
                unresolved_parts: list[str] = []
                variant_mismatch_parts: list[str] = []
                discrepancy = False
                for raw_part in evidence["raw_part_codes"]:
                    canonical = CANONICAL_PART_ALIASES.get(raw_part, raw_part)
                    canonical_parts.append(canonical)
                    if canonical != raw_part:
                        discrepancy = True
                        add_issue(
                            issues,
                            "PART_CODE_SOURCE_DISCREPANCY",
                            "WARNING",
                            f"Layout source {raw_part} is retained and normalized to canonical {canonical}.",
                            machine_code=spec.machine_code,
                            variant_codes=list(spec.variant_codes),
                            raw_part_code=raw_part,
                            canonical_part_code=canonical,
                            source=source,
                        )
                    if canonical not in authoritative_parts:
                        unresolved_parts.append(canonical)
                        add_issue(
                            issues,
                            "SOURCE_PART_NOT_IN_TEXTUAL_MASTER",
                            "WARNING",
                            "Layout evidence is retained but does not create a new canonical part.",
                            machine_code=spec.machine_code,
                            variant_codes=list(spec.variant_codes),
                            raw_part_code=raw_part,
                            canonical_part_code=canonical,
                            source=source,
                        )
                    else:
                        resolved_parts.append(canonical)
                    if canonical in authoritative_parts and canonical not in planned_parts:
                        variant_mismatch_parts.append(canonical)
                        add_issue(
                            issues,
                            "SOURCE_PART_NOT_PLANNED_FOR_LAYOUT_VARIANT",
                            "WARNING",
                            "Part exists in master data but is not planned for this layout's assigned machine/variant context.",
                            machine_code=spec.machine_code,
                            variant_codes=list(spec.variant_codes),
                            part_code=canonical,
                            source=source,
                        )

                canonical_parts = sorted(set(canonical_parts))
                resolved_parts = sorted(set(resolved_parts))
                unresolved_parts = sorted(set(unresolved_parts))
                variant_mismatch_parts = sorted(set(variant_mismatch_parts))
                part_unresolved = not evidence["raw_part_codes"] or bool(unresolved_parts)
                if part_unresolved:
                    add_issue(
                        issues,
                        "PART_ASSOCIATION_UNRESOLVED",
                        "INFO",
                        "Slot geometry is imported, but its part association is blank, unknown or outside the proven variant context.",
                        machine_code=spec.machine_code,
                        variant_codes=list(spec.variant_codes),
                        layout_code=spec.code,
                        raw_part_codes=evidence["raw_part_codes"],
                        unresolved_part_codes=unresolved_parts,
                        source=source,
                    )

                container = derive_container(
                    spec.machine_code,
                    spec.variant_codes,
                    resolved_parts,
                    evidence["explicit_container_codes"],
                    planning,
                    allow_cross_context=not variant_mismatch_parts,
                )
                if container["evidence"] == "phase_02a_exact_part_cross_context":
                    add_issue(
                        issues,
                        "CONTAINER_MAPPING_DERIVED_FROM_EXACT_PART",
                        "INFO",
                        "The slot's exact part code has one consistent container code in another customer planning context; the relationship is reused without inferring from category or geometry.",
                        machine_code=spec.machine_code,
                        variant_codes=list(spec.variant_codes),
                        layout_code=spec.code,
                        resolved_part_codes=resolved_parts,
                        container_code=container["container_code"],
                        planning_source_refs=container["planning_source_refs"],
                        source=source,
                    )
                if evidence["unknown_container_tokens"]:
                    add_issue(
                        issues,
                        "UNKNOWN_CONTAINER_MAPPING",
                        "WARNING",
                        "Unknown layout container token is reported and is not guessed or inserted.",
                        machine_code=spec.machine_code,
                        layout_code=spec.code,
                        unknown_container_tokens=evidence["unknown_container_tokens"],
                        source=source,
                    )
                if container["ambiguous"]:
                    add_issue(
                        issues,
                        "CONTAINER_MAPPING_AMBIGUOUS",
                        "WARNING",
                        "Multiple source-backed container candidates exist; no default container is selected.",
                        machine_code=spec.machine_code,
                        layout_code=spec.code,
                        explicit_container_codes=evidence["explicit_container_codes"],
                        planning_container_candidates=container["planning_container_candidates"],
                        source=source,
                    )
                if (
                    container["evidence"] == "layout_label"
                    and container["planning_container_candidates"]
                    and container["container_code"] not in container["planning_container_candidates"]
                ):
                    add_issue(
                        issues,
                        "LAYOUT_CONTAINER_PLANNING_CONFLICT",
                        "WARNING",
                        "Explicit layout container evidence conflicts with Phase 02A planning; layout evidence is retained without changing textual master data.",
                        machine_code=spec.machine_code,
                        layout_code=spec.code,
                        layout_container_code=container["container_code"],
                        planning_container_candidates=container["planning_container_candidates"],
                        source=source,
                    )
                container_unresolved = container["container_code"] is None
                if container_unresolved:
                    add_issue(
                        issues,
                        "CONTAINER_ASSOCIATION_UNRESOLVED",
                        "INFO",
                        "Slot is imported with NULL default_container_type_id; no unique source-backed mapping exists.",
                        machine_code=spec.machine_code,
                        variant_codes=list(spec.variant_codes),
                        layout_code=spec.code,
                        resolved_part_codes=resolved_parts,
                        source=source,
                    )

                footprint = None
                footprint_evidence = None
                footprint_source_refs: list[dict[str, Any]] = []
                if len(evidence["explicit_dimensions_mm"]) == 1:
                    footprint = evidence["explicit_dimensions_mm"][0]
                    footprint_evidence = "layout_label"
                    footprint_source_refs = [source]
                elif container["container_code"]:
                    container_record = container_records[container["container_code"]]
                    if all(
                        container_record.get(field) is not None
                        for field in ("length_mm", "width_mm", "height_mm")
                    ):
                        footprint = {
                            "length_mm": container_record["length_mm"],
                            "width_mm": container_record["width_mm"],
                            "height_mm": container_record["height_mm"],
                        }
                        footprint_evidence = "phase_02a_container_master"
                        footprint_source_refs = container_record.get("dimension_source_refs", [])
                confidence = (
                    "HIGH"
                    if evidence["raw_part_codes"]
                    and not unresolved_parts
                    and not variant_mismatch_parts
                    and not discrepancy
                    else "MEDIUM"
                )
                code = f"SLOT-{ordinal:03d}"
                label_parts = resolved_parts or canonical_parts
                display_suffix = " / ".join(label_parts) if label_parts else "unresolved"
                slot = {
                    "layout_key": key,
                    "machine_code": spec.machine_code,
                    "layout_code": spec.code,
                    "code": code,
                    "display_name": f"{code} — {display_suffix}",
                    "slot_role": "OTHER",
                    "default_container_code": container["container_code"],
                    "display_order": ordinal,
                    "allows_empty_load": True,
                    "allows_full_load": True,
                    "is_enabled": True,
                    "confidence": confidence,
                    "part_association_unresolved": part_unresolved,
                    "container_association_unresolved": container_unresolved,
                    "physical_footprint_unknown": footprint is None,
                    **normalized_geometry(sheet, spec.canvas_range, cell_range),
                    "metadata": {
                        "managed_by": "phase_02b",
                        "schema_version": SCHEMA_VERSION,
                        "confidence": confidence,
                        "source": source,
                        "source_kind": "layout_slot",
                        "raw_label": evidence["raw_label"],
                        "raw_part_codes": evidence["raw_part_codes"],
                        "canonical_part_codes": canonical_parts,
                        "resolved_part_codes": resolved_parts,
                        "unresolved_part_codes": unresolved_parts,
                        "variant_mismatch_part_codes": variant_mismatch_parts,
                        "part_association_unresolved": part_unresolved,
                        "explicit_container_codes": evidence["explicit_container_codes"],
                        "unknown_container_tokens": evidence["unknown_container_tokens"],
                        "container_code": container["container_code"],
                        "container_evidence": container["evidence"],
                        "planning_container_candidates": container["planning_container_candidates"],
                        "planning_source_refs": container["planning_source_refs"],
                        "container_association_unresolved": container_unresolved,
                        "physical_footprint_mm": footprint,
                        "physical_footprint_evidence": footprint_evidence,
                        "physical_footprint_source_refs": footprint_source_refs,
                        "geometry_basis": "excel_cell_display_grid",
                        "ignored_label_tokens": evidence["ignored_label_tokens"],
                    },
                }
                layout_slots.append(slot)

            missing_footprints = sum(item["physical_footprint_unknown"] for item in layout_slots)
            if missing_footprints:
                add_issue(
                    issues,
                    "PHYSICAL_FOOTPRINT_NOT_PROVIDED",
                    "INFO",
                    "No source-backed nominal container dimensions are available for these regions; Excel display geometry is not converted to millimetres.",
                    machine_code=spec.machine_code,
                    layout_code=spec.code,
                    affected_slot_count=missing_footprints,
                    source=layout["source"],
                )
            validate_layout_geometry(layout, layout_slots)
            slots.extend(layout_slots)
    finally:
        for workbook in workbooks.values():
            workbook.close()

    for candidate in excluded_candidates:
        add_issue(
            issues,
            "LOW_CONFIDENCE_CANDIDATE_EXCLUDED",
            "INFO",
            candidate["reason"],
            source=candidate["source"],
            raw_label=candidate["raw_label"],
        )
    add_issue(
        issues,
        "SEALER_VARIANT_3_LAYOUT_UNPROVEN",
        "INFO",
        "The combined physical-layout workbook proves a SEALER region with V1/V2 part evidence, but no V3 part or layout evidence; V3 remains unassigned.",
        machine_code="SEALER",
        variant_code="3",
        source=range_payload(project_root, SHARED, "1. VARYANT", "CG1:FH138"),
    )

    manifest_after = build_manifest(project_root)
    if manifest_before != manifest_after:
        raise StructuralExtractionError("Customer source files or the frozen Phase 02A baseline changed during extraction")

    layouts = sorted(layouts, key=lambda item: (item["machine_code"], item["code"]))
    assignments = sorted(
        assignments,
        key=lambda item: (item["machine_code"], item["variant_code"], item["layout_code"]),
    )
    slots = sorted(slots, key=lambda item: (item["machine_code"], item["layout_code"], item["display_order"], item["code"]))
    issues_list = [issues[key] for key in sorted(issues)]
    issue_codes = Counter(item["code"] for item in issues_list)
    issue_severities = Counter(item["severity"] for item in issues_list)
    accepted_confidence = Counter(item["confidence"] for item in slots)
    candidate_confidence = Counter(accepted_confidence)
    candidate_confidence.update(item["confidence"] for item in excluded_candidates)
    layout_counts = Counter(item["machine_code"] for item in layouts)
    slot_counts: dict[str, int] = dict(Counter(item["layout_key"] for item in slots))
    unresolved_part_slots = [
        {"layout_key": item["layout_key"], "slot_code": item["code"], "source": item["metadata"]["source"]}
        for item in slots
        if item["slot_role"] != "MACHINE" and item["part_association_unresolved"]
    ]
    unresolved_container_slots = [
        {"layout_key": item["layout_key"], "slot_code": item["code"], "source": item["metadata"]["source"]}
        for item in slots
        if item["slot_role"] != "MACHINE" and item["container_association_unresolved"]
    ]
    uninterpreted_regions = [
        {"source": item["source"], "reason": item["reason"]}
        for item in excluded_candidates
    ] + [
        {
            "source": range_payload(project_root, SHARED, "1. VARYANT", "CG1:FH138"),
            "reason": "SEALER V3 has no physical-layout or part evidence and is intentionally unassigned.",
        }
    ]
    counts = {
        "customer_workbooks": len(SOURCE_FILES),
        "layouts": len(layouts),
        "layout_assignments": len(assignments),
        "layout_slots": len(slots),
        "machine_region_slots": sum(item["slot_role"] == "MACHINE" for item in slots),
        "logistics_slots": sum(item["slot_role"] != "MACHINE" for item in slots),
        "high_confidence_slots": accepted_confidence.get("HIGH", 0),
        "medium_confidence_slots": accepted_confidence.get("MEDIUM", 0),
        "low_confidence_slots": accepted_confidence.get("LOW", 0),
        "low_confidence_excluded_candidates": len(excluded_candidates),
        "unresolved_part_association_slots": len(unresolved_part_slots),
        "unresolved_container_association_slots": len(unresolved_container_slots),
        "unknown_physical_footprints": sum(item["physical_footprint_unknown"] for item in slots),
        "unknown_logistics_slot_footprints": sum(
            item["physical_footprint_unknown"] and item["slot_role"] != "MACHINE" for item in slots
        ),
        "unknown_machine_region_footprints": sum(
            item["physical_footprint_unknown"] and item["slot_role"] == "MACHINE" for item in slots
        ),
        "data_issues": len(issues_list),
    }
    container_footprint_counts = Counter(
        item["default_container_code"] or "(unresolved)"
        for item in slots
        if item["slot_role"] != "MACHINE" and item["physical_footprint_unknown"]
    )
    return {
        "schema_version": SCHEMA_VERSION,
        "source_digest": manifest_digest(manifest_before),
        "source_files_unchanged": True,
        "manifest": manifest_before,
        "precedence": [
            "Colored physical-layout workbooks are authoritative for display geometry and explicit labels.",
            "Phase 02A is the canonical source for machine/variant/part/container identity, planning mappings and reconciled customer container-master evidence.",
            "Explicit layout container labels outrank Phase 02A-derived container candidates but conflicts are reported.",
            "An exact part code may reuse one globally consistent customer planning container mapping when the local row is blank and the slot has no variant mismatch.",
            "Excel cell size is normalized only for PDA display; it is not a millimetre, robot, map, route or waypoint coordinate.",
            "No new canonical part or container is created from layout-only evidence.",
        ],
        "counts": counts,
        "layout_counts_by_machine": dict(sorted(layout_counts.items())),
        "slot_counts_by_layout": dict(sorted(slot_counts.items())),
        "accepted_confidence_counts": {level: accepted_confidence.get(level, 0) for level in ("HIGH", "MEDIUM", "LOW")},
        "candidate_confidence_counts": {level: candidate_confidence.get(level, 0) for level in ("HIGH", "MEDIUM", "LOW")},
        "issue_counts_by_code": dict(sorted(issue_codes.items())),
        "issue_counts_by_severity": dict(sorted(issue_severities.items())),
        "container_footprint_issue_counts": dict(sorted(container_footprint_counts.items())),
        "unresolved_part_slots": unresolved_part_slots,
        "unresolved_container_slots": unresolved_container_slots,
        "uninterpreted_source_regions": uninterpreted_regions,
        "sealer_status": {
            "layout_seeded": True,
            "shared_variant_codes": ["1", "2"],
            "unassigned_variant_codes": ["3"],
            "conclusion": "The combined customer workbook explicitly labels a SEALER physical region and contains V1/V2 part evidence in the same geometry; V3 remains unassigned because no physical evidence exists.",
        },
        "layouts": layouts,
        "variant_layout_assignments": assignments,
        "layout_slots": slots,
        "excluded_candidates": sorted(
            excluded_candidates,
            key=lambda item: (
                item["source"]["file"], item["source"]["sheet"], item["source"]["range"]
            ),
        ),
        "data_issues": issues_list,
    }


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = (
        Path("C:/Windows/Fonts/segoeuib.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf"),
        Path("C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf"),
    )
    for candidate in candidates:
        if candidate.is_file():
            return ImageFont.truetype(str(candidate), size=size)
    return ImageFont.load_default()


def fit_lines(text: str, width: int, height: int) -> tuple[list[str], ImageFont.ImageFont]:
    for size in (18, 16, 14, 12, 10):
        font = load_font(size, bold=size >= 14)
        approximate_chars = max(5, int(width / max(size * 0.58, 1)))
        lines: list[str] = []
        for logical in text.split("\n"):
            lines.extend(textwrap.wrap(logical, width=approximate_chars) or [""])
        line_height = size + 3
        if len(lines) * line_height <= max(8, height - 8):
            return lines[: max(1, (height - 8) // line_height)], font
    return [text[:24]], load_font(10)


def render_review_image(
    project_root: Path,
    output_dir: Path,
    layout: dict[str, Any],
    layout_slots: list[dict[str, Any]],
) -> None:
    width, height = 1800, 1100
    plot_left, plot_top, plot_right, plot_bottom = 50, 160, 1750, 1025
    plot_width = plot_right - plot_left
    plot_height = plot_bottom - plot_top
    image = Image.new("RGB", (width, height), "#F8FAFC")
    draw = ImageDraw.Draw(image)
    title_font = load_font(30, bold=True)
    meta_font = load_font(16)
    legend_font = load_font(15, bold=True)

    draw.text((50, 28), f"{layout['machine_code']} / {layout['name']}", fill="#0F172A", font=title_font)
    draw.text(
        (50, 72),
        f"Variants: {', '.join(layout['variant_codes'])}   |   Source: {source_location(layout['source'])}",
        fill="#334155",
        font=meta_font,
    )
    legend = (("HIGH", "#DCFCE7", "#166534"), ("MEDIUM", "#FEF3C7", "#92400E"), ("MACHINE", "#E2E8F0", "#334155"))
    legend_x = 50
    for label, fill, outline in legend:
        draw.rectangle((legend_x, 112, legend_x + 24, 136), fill=fill, outline=outline, width=2)
        draw.text((legend_x + 32, 111), label, fill="#0F172A", font=legend_font)
        legend_x += 155

    draw.rectangle((plot_left, plot_top, plot_right, plot_bottom), fill="#FFFFFF", outline="#64748B", width=2)
    for step in range(1, 10):
        x = plot_left + round(plot_width * step / 10)
        y = plot_top + round(plot_height * step / 10)
        draw.line((x, plot_top, x, plot_bottom), fill="#E2E8F0", width=1)
        draw.line((plot_left, y, plot_right, y), fill="#E2E8F0", width=1)

    for slot in sorted(layout_slots, key=lambda item: (item["slot_role"] != "MACHINE", item["display_order"])):
        left = plot_left + round(slot["ui_x"] * plot_width)
        top = plot_top + round(slot["ui_y"] * plot_height)
        right = plot_left + round((slot["ui_x"] + slot["ui_width"]) * plot_width)
        bottom = plot_top + round((slot["ui_y"] + slot["ui_height"]) * plot_height)
        right = max(right, left + 3)
        bottom = max(bottom, top + 3)
        if slot["slot_role"] == "MACHINE":
            fill, outline = "#E2E8F0", "#334155"
            label = f"MACHINE\n{layout['machine_code']}"
        elif slot["confidence"] == "HIGH":
            fill, outline = "#DCFCE7", "#166534"
            parts = slot["metadata"]["resolved_part_codes"]
            container = slot["default_container_code"] or "container ?"
            label = f"{slot['code']} [H]\n" + (" / ".join(parts) if parts else "part ?") + f"\n{container}"
        else:
            fill, outline = "#FEF3C7", "#92400E"
            parts = slot["metadata"]["resolved_part_codes"] or slot["metadata"]["canonical_part_codes"]
            container = slot["default_container_code"] or "container ?"
            label = f"{slot['code']} [M]\n" + (" / ".join(parts) if parts else "part ?") + f"\n{container}"
        draw.rectangle((left, top, right, bottom), fill=fill, outline=outline, width=3)
        rect_width, rect_height = right - left, bottom - top
        lines, font = fit_lines(label, rect_width, rect_height)
        line_height = getattr(font, "size", 10) + 3
        text_height = len(lines) * line_height
        y = top + max(4, (rect_height - text_height) // 2)
        for line in lines:
            bbox = draw.textbbox((0, 0), line, font=font)
            text_width = bbox[2] - bbox[0]
            x = left + max(3, (rect_width - text_width) // 2)
            draw.text((x, y), line, fill="#0F172A", font=font)
            y += line_height

    footer_font = load_font(14)
    draw.text(
        (50, 1050),
        "Normalized 0.0–1.0 PDA display geometry. Physical footprint is NULL unless explicitly stated; no robot/map coordinate is shown.",
        fill="#475569",
        font=footer_font,
    )
    destination = output_dir / Path(layout["review_image"]).relative_to("data/generated/phase_02b")
    destination.parent.mkdir(parents=True, exist_ok=True)
    image.save(destination, format="PNG", optimize=False, compress_level=9)


def sql_literal(value: Any) -> str:
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "true" if value else "false"
    return "'" + str(value).replace("'", "''") + "'"


def sql_number(value: float | int) -> str:
    if isinstance(value, int):
        return str(value)
    return f"{value:.4f}"


def values_clause(rows: Iterable[Iterable[str]]) -> str:
    return ",\n".join("    (" + ", ".join(row) + ")" for row in rows)


def generate_seed_sql(dataset: dict[str, Any]) -> str:
    layout_values = values_clause(
        (
            sql_literal(item["machine_code"]),
            sql_literal(item["code"]),
            sql_literal(item["name"]),
            str(item["version"]),
            sql_literal(item["status"]),
        )
        for item in dataset["layouts"]
    )
    assignment_values = values_clause(
        (
            sql_literal(item["machine_code"]),
            sql_literal(item["variant_code"]),
            sql_literal(item["layout_code"]),
            sql_literal(item["is_default"]),
        )
        for item in dataset["variant_layout_assignments"]
    )
    slot_values = values_clause(
        (
            sql_literal(item["machine_code"]),
            sql_literal(item["layout_code"]),
            sql_literal(item["code"]),
            sql_literal(item["display_name"]),
            sql_literal(item["slot_role"]),
            sql_literal(item["default_container_code"]),
            sql_number(item["ui_x"]),
            sql_number(item["ui_y"]),
            sql_number(item["ui_width"]),
            sql_number(item["ui_height"]),
            str(item["display_order"]),
            sql_literal(item["allows_empty_load"]),
            sql_literal(item["allows_full_load"]),
            sql_literal(item["is_enabled"]),
            sql_literal(json.dumps(item["metadata"], ensure_ascii=False, sort_keys=True, separators=(",", ":"))),
        )
        for item in dataset["layout_slots"]
    )
    return f"""-- TKG PDA Phase 02B generated customer physical layouts
-- schema_version: {dataset['schema_version']}
-- source_digest: {dataset['source_digest']}
-- layouts: {dataset['counts']['layouts']}; assignments: {dataset['counts']['layout_assignments']}; slots: {dataset['counts']['layout_slots']}
-- Excel UI geometry only; no robot, map, route, waypoint or RDS identifiers
-- deterministic output; do not edit by hand

BEGIN;

WITH source(machine_code, layout_code, layout_name, version, status) AS (
VALUES
{layout_values}
)
INSERT INTO master.layouts (machine_id, code, name, version, status, published_at)
SELECT m.id, s.layout_code, s.layout_name, s.version, s.status,
       CASE WHEN s.status = 'PUBLISHED' THEN now() ELSE NULL END
FROM source s
JOIN master.machines m ON m.code = s.machine_code
ON CONFLICT (machine_id, code, version) DO UPDATE
SET name = EXCLUDED.name,
    status = EXCLUDED.status
WHERE master.layouts.name IS DISTINCT FROM EXCLUDED.name
   OR master.layouts.status IS DISTINCT FROM EXCLUDED.status;

WITH source(machine_code, variant_code, layout_code, is_default) AS (
VALUES
{assignment_values}
), resolved AS (
    SELECT v.id AS variant_id, m.id AS machine_id, l.id AS layout_id, s.is_default
    FROM source s
    JOIN master.machines m ON m.code = s.machine_code
    JOIN master.variants v ON v.machine_id = m.id AND v.code = s.variant_code
    JOIN master.layouts l ON l.machine_id = m.id AND l.code = s.layout_code AND l.version = 1
)
INSERT INTO master.variant_layout_assignments (variant_id, machine_id, layout_id, is_default)
SELECT variant_id, machine_id, layout_id, is_default
FROM resolved
ON CONFLICT (variant_id, layout_id) DO UPDATE
SET machine_id = EXCLUDED.machine_id,
    is_default = EXCLUDED.is_default
WHERE master.variant_layout_assignments.machine_id IS DISTINCT FROM EXCLUDED.machine_id
   OR master.variant_layout_assignments.is_default IS DISTINCT FROM EXCLUDED.is_default;

WITH source(
    machine_code, layout_code, slot_code, display_name, slot_role, container_code,
    ui_x, ui_y, ui_width, ui_height, display_order,
    allows_empty_load, allows_full_load, is_enabled, metadata_text
) AS (
VALUES
{slot_values}
), resolved AS (
    SELECT l.id AS layout_id, s.slot_code, s.display_name, s.slot_role,
           ct.id AS container_type_id, s.ui_x, s.ui_y, s.ui_width, s.ui_height,
           s.display_order, s.allows_empty_load, s.allows_full_load, s.is_enabled,
           s.metadata_text::jsonb AS metadata_json
    FROM source s
    JOIN master.machines m ON m.code = s.machine_code
    JOIN master.layouts l ON l.machine_id = m.id AND l.code = s.layout_code AND l.version = 1
    LEFT JOIN master.container_types ct ON ct.code = s.container_code
)
INSERT INTO master.layout_slots (
    layout_id, code, display_name, slot_role, default_container_type_id,
    ui_x, ui_y, ui_width, ui_height, display_order,
    allows_empty_load, allows_full_load, is_enabled, metadata_json
)
SELECT layout_id, slot_code, display_name, slot_role, container_type_id,
       ui_x, ui_y, ui_width, ui_height, display_order,
       allows_empty_load, allows_full_load, is_enabled, metadata_json
FROM resolved
ON CONFLICT (layout_id, code) DO UPDATE
SET display_name = EXCLUDED.display_name,
    slot_role = EXCLUDED.slot_role,
    default_container_type_id = EXCLUDED.default_container_type_id,
    ui_x = EXCLUDED.ui_x,
    ui_y = EXCLUDED.ui_y,
    ui_width = EXCLUDED.ui_width,
    ui_height = EXCLUDED.ui_height,
    display_order = EXCLUDED.display_order,
    allows_empty_load = EXCLUDED.allows_empty_load,
    allows_full_load = EXCLUDED.allows_full_load,
    is_enabled = EXCLUDED.is_enabled,
    metadata_json = EXCLUDED.metadata_json
WHERE ROW(
    master.layout_slots.display_name, master.layout_slots.slot_role,
    master.layout_slots.default_container_type_id, master.layout_slots.ui_x,
    master.layout_slots.ui_y, master.layout_slots.ui_width, master.layout_slots.ui_height,
    master.layout_slots.display_order, master.layout_slots.allows_empty_load,
    master.layout_slots.allows_full_load, master.layout_slots.is_enabled,
    master.layout_slots.metadata_json
) IS DISTINCT FROM ROW(
    EXCLUDED.display_name, EXCLUDED.slot_role, EXCLUDED.default_container_type_id,
    EXCLUDED.ui_x, EXCLUDED.ui_y, EXCLUDED.ui_width, EXCLUDED.ui_height,
    EXCLUDED.display_order, EXCLUDED.allows_empty_load, EXCLUDED.allows_full_load,
    EXCLUDED.is_enabled, EXCLUDED.metadata_json
);

COMMIT;
"""


def eligible_slot_part_pairs(dataset: dict[str, Any]) -> list[dict[str, str]]:
    """Curated (machine_code, layout_code, slot_code, part_code) rows for
    master.layout_slot_parts — the ONLY input the automatic machine-slot
    resolver may use.

    A pair is included only when the slot's part evidence is fully resolved
    (part_association_unresolved is False) AND the resolved part is not
    flagged as a variant mismatch (present in master data but not planned
    for this layout's machine/variant). Both are genuine ambiguity signals
    the pipeline already computes from the customer source; anything short
    of "resolved and variant-consistent" is left unmapped rather than
    guessed, per the same standard this pipeline already holds itself to for
    parts and containers.
    """

    pairs: list[dict[str, str]] = []
    for slot in dataset["layout_slots"]:
        if slot["slot_role"] == "MACHINE" or slot["part_association_unresolved"]:
            continue
        metadata = slot["metadata"]
        mismatched = set(metadata.get("variant_mismatch_part_codes", ()))
        for part_code in metadata.get("resolved_part_codes", ()):
            if part_code in mismatched:
                continue
            pairs.append(
                {
                    "machine_code": slot["machine_code"],
                    "layout_code": slot["layout_code"],
                    "slot_code": slot["code"],
                    "part_code": part_code,
                }
            )
    pairs.sort(key=lambda row: (row["machine_code"], row["layout_code"], row["slot_code"], row["part_code"]))
    return pairs


def generate_slot_part_seed_sql(dataset: dict[str, Any], pairs: list[dict[str, str]]) -> str:
    pair_values = values_clause(
        (
            sql_literal(row["machine_code"]),
            sql_literal(row["layout_code"]),
            sql_literal(row["slot_code"]),
            sql_literal(row["part_code"]),
        )
        for row in pairs
    )
    logistics_slots = [slot for slot in dataset["layout_slots"] if slot["slot_role"] != "MACHINE"]
    mapped_slot_keys = {
        (row["machine_code"], row["layout_code"], row["slot_code"])
        for row in pairs
    }
    unmapped = sum(
        1
        for slot in logistics_slots
        if (slot["machine_code"], slot["layout_code"], slot["code"]) not in mapped_slot_keys
    )
    unresolved = sum(1 for slot in logistics_slots if slot["part_association_unresolved"])
    variant_mismatch_only = unmapped - unresolved
    return f"""-- TKG PDA Phase 02B generated Part <-> Layout Slot assignments
-- schema_version: {dataset['schema_version']}
-- source_digest: {dataset['source_digest']}
-- curated pairs: {len(pairs)}; mapped logistics slots: {len(mapped_slot_keys)}
-- unmapped logistics slots: {unmapped} ({unresolved} unresolved evidence; {variant_mismatch_only} variant-mismatch-only)
-- Populated ONLY where the customer-derived source relationship is
-- unambiguous (part_association_unresolved = false and the resolved part
-- is not a variant mismatch); everything else remains unmapped rather than
-- guessed. See db/migrations/005_layout_slot_part_mapping.sql.
-- deterministic output; do not edit by hand

BEGIN;

WITH source(machine_code, layout_code, slot_code, part_code) AS (
VALUES
{pair_values}
), resolved AS (
    SELECT ls.id AS layout_slot_id, p.id AS part_id
    FROM source s
    JOIN master.machines m ON m.code = s.machine_code
    JOIN master.layouts l ON l.machine_id = m.id AND l.code = s.layout_code AND l.version = 1
    JOIN master.layout_slots ls ON ls.layout_id = l.id AND ls.code = s.slot_code
    JOIN master.parts p ON p.part_code = s.part_code
)
INSERT INTO master.layout_slot_parts (layout_slot_id, part_id)
SELECT layout_slot_id, part_id FROM resolved
ON CONFLICT (layout_slot_id, part_id) DO NOTHING;

COMMIT;
"""


def write_json(path: Path, payload: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def write_csv(path: Path, fieldnames: Sequence[str], rows: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, lineterminator="\n")
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def run_pipeline(
    project_root: Path,
    output_dir: Path,
    seed_path: Path,
    slot_part_seed_path: Path,
) -> dict[str, Any]:
    project_root = project_root.resolve()
    output_dir = output_dir.resolve()
    seed_path = seed_path.resolve()
    dataset = build_dataset(project_root)
    output_dir.mkdir(parents=True, exist_ok=True)
    for layout in dataset["layouts"]:
        render_review_image(
            project_root,
            output_dir,
            layout,
            [slot for slot in dataset["layout_slots"] if slot["layout_key"] == layout["layout_key"]],
        )

    write_json(output_dir / "normalized_physical_layouts.json", dataset)
    write_json(output_dir / "source_manifest.json", dataset["manifest"])
    summary = {
        key: dataset[key]
        for key in (
            "schema_version", "source_digest", "source_files_unchanged", "counts",
            "layout_counts_by_machine", "slot_counts_by_layout", "accepted_confidence_counts",
            "candidate_confidence_counts", "issue_counts_by_code", "issue_counts_by_severity",
            "container_footprint_issue_counts",
            "unresolved_part_slots", "unresolved_container_slots", "uninterpreted_source_regions",
            "sealer_status",
        )
    }
    write_json(output_dir / "summary.json", summary)
    write_json(output_dir / "data_issues.json", dataset["data_issues"])

    write_csv(
        output_dir / "layouts.csv",
        ("machine_code", "code", "name", "version", "status", "variant_codes", "source_file", "source_sheet", "source_range", "review_image"),
        (
            {
                "machine_code": item["machine_code"], "code": item["code"], "name": item["name"],
                "version": item["version"], "status": item["status"],
                "variant_codes": ",".join(item["variant_codes"]),
                "source_file": item["source"]["file"], "source_sheet": item["source"]["sheet"],
                "source_range": item["source"]["range"], "review_image": item["review_image"],
            }
            for item in dataset["layouts"]
        ),
    )
    write_csv(
        output_dir / "variant_layout_assignments.csv",
        ("machine_code", "variant_code", "layout_code", "is_default", "source_file", "source_sheet", "source_range"),
        (
            {
                "machine_code": item["machine_code"], "variant_code": item["variant_code"],
                "layout_code": item["layout_code"], "is_default": item["is_default"],
                "source_file": item["source"]["file"], "source_sheet": item["source"]["sheet"],
                "source_range": item["source"]["range"],
            }
            for item in dataset["variant_layout_assignments"]
        ),
    )
    write_csv(
        output_dir / "layout_slots.csv",
        (
            "machine_code", "layout_code", "slot_code", "display_name", "slot_role",
            "default_container_code", "ui_x", "ui_y", "ui_width", "ui_height", "display_order",
            "confidence", "part_association_unresolved", "container_association_unresolved",
            "physical_footprint_unknown", "source_file", "source_sheet", "source_range", "metadata_json",
        ),
        (
            {
                "machine_code": item["machine_code"], "layout_code": item["layout_code"],
                "slot_code": item["code"], "display_name": item["display_name"],
                "slot_role": item["slot_role"], "default_container_code": item["default_container_code"],
                "ui_x": f"{item['ui_x']:.4f}", "ui_y": f"{item['ui_y']:.4f}",
                "ui_width": f"{item['ui_width']:.4f}", "ui_height": f"{item['ui_height']:.4f}",
                "display_order": item["display_order"], "confidence": item["confidence"],
                "part_association_unresolved": item["part_association_unresolved"],
                "container_association_unresolved": item["container_association_unresolved"],
                "physical_footprint_unknown": item["physical_footprint_unknown"],
                "source_file": item["metadata"]["source"]["file"],
                "source_sheet": item["metadata"]["source"]["sheet"],
                "source_range": item["metadata"]["source"]["range"],
                "metadata_json": json.dumps(item["metadata"], ensure_ascii=False, sort_keys=True, separators=(",", ":")),
            }
            for item in dataset["layout_slots"]
        ),
    )
    write_csv(
        output_dir / "excluded_candidates.csv",
        ("confidence", "status", "reason", "source_file", "source_sheet", "source_range", "raw_label", "has_outer_border"),
        (
            {
                "confidence": item["confidence"], "status": item["status"], "reason": item["reason"],
                "source_file": item["source"]["file"], "source_sheet": item["source"]["sheet"],
                "source_range": item["source"]["range"], "raw_label": item["raw_label"],
                "has_outer_border": item["has_outer_border"],
            }
            for item in dataset["excluded_candidates"]
        ),
    )
    issue_fields = ("issue_id", "code", "severity", "blocking", "message", "context_json")
    write_csv(
        output_dir / "data_issues.csv",
        issue_fields,
        (
            {
                "issue_id": item["issue_id"], "code": item["code"], "severity": item["severity"],
                "blocking": item["blocking"], "message": item["message"],
                "context_json": json.dumps(
                    {key: value for key, value in item.items() if key not in issue_fields[:-1]},
                    ensure_ascii=False, sort_keys=True, separators=(",", ":"),
                ),
            }
            for item in dataset["data_issues"]
        ),
    )
    seed_path.parent.mkdir(parents=True, exist_ok=True)
    seed_path.write_text(generate_seed_sql(dataset), encoding="utf-8", newline="\n")

    slot_part_pairs = eligible_slot_part_pairs(dataset)
    dataset["slot_part_pairs"] = slot_part_pairs
    write_csv(
        output_dir / "layout_slot_part_assignments.csv",
        ("machine_code", "layout_code", "slot_code", "part_code"),
        slot_part_pairs,
    )
    slot_part_seed_path.parent.mkdir(parents=True, exist_ok=True)
    slot_part_seed_path.write_text(
        generate_slot_part_seed_sql(dataset, slot_part_pairs), encoding="utf-8", newline="\n"
    )

    if build_manifest(project_root) != dataset["manifest"]:
        raise StructuralExtractionError("Source files changed while generated outputs were being written")
    return dataset


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate TKG PDA Phase 02B physical-layout artifacts")
    parser.add_argument("--project-root", type=Path, default=Path("."))
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--seed-path", type=Path)
    parser.add_argument("--slot-part-seed-path", type=Path)
    args = parser.parse_args()
    project_root = args.project_root.resolve()
    output_dir = args.output_dir or project_root / "data/generated/phase_02b"
    seed_path = args.seed_path or project_root / "db/seeds/generated/002b_customer_physical_layouts.sql"
    slot_part_seed_path = (
        args.slot_part_seed_path
        or project_root / "db/seeds/generated/002c_customer_layout_slot_part_assignments.sql"
    )
    dataset = run_pipeline(project_root, output_dir, seed_path, slot_part_seed_path)
    print(json.dumps(dataset["counts"], ensure_ascii=False, sort_keys=True))


if __name__ == "__main__":
    main()
