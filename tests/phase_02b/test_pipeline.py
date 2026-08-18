from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(PROJECT_ROOT))

from tools.phase_02b.pipeline import (  # noqa: E402
    KNOWN_MACHINES,
    build_dataset,
    build_manifest,
    normalize_identifier,
    normalize_label,
    rectangles_overlap,
    run_pipeline,
)


def directory_hashes(root: Path) -> dict[str, str]:
    return {
        path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


class Phase02BPipelineTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest_before = build_manifest(PROJECT_ROOT)
        cls.dataset = build_dataset(PROJECT_ROOT)
        cls.phase02a = json.loads(
            (PROJECT_ROOT / "data/generated/phase_02a/normalized_customer_data.json").read_text(encoding="utf-8")
        )

    def test_01_normalization_is_deterministic(self) -> None:
        self.assertEqual(normalize_identifier(" 67115bn050–10–haos "), "67115BN050-10-HAOS")
        self.assertEqual(normalize_label("4322-MİX PALET"), "4322 MIX PALET")

    def test_02_rerun_is_byte_equivalent_including_png_and_sql(self) -> None:
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            first_root, second_root = Path(first), Path(second)
            run_pipeline(PROJECT_ROOT, first_root / "output", first_root / "seed.sql")
            run_pipeline(PROJECT_ROOT, second_root / "output", second_root / "seed.sql")
            self.assertEqual(directory_hashes(first_root), directory_hashes(second_root))

    def test_03_all_layouts_use_the_known_nine_machines(self) -> None:
        machines = {item["machine_code"] for item in self.dataset["layouts"]}
        self.assertEqual(machines, set(KNOWN_MACHINES))

    def test_04_assignments_are_same_machine_and_shared_layouts_work(self) -> None:
        layout_machines = {item["layout_key"]: item["machine_code"] for item in self.dataset["layouts"]}
        for item in self.dataset["variant_layout_assignments"]:
            self.assertEqual(layout_machines[item["layout_key"]], item["machine_code"])
            self.assertIn(item["variant_code"], KNOWN_MACHINES[item["machine_code"]])
        by_layout: dict[str, set[str]] = {}
        for item in self.dataset["variant_layout_assignments"]:
            by_layout.setdefault(item["layout_key"], set()).add(item["variant_code"])
        for machine in ("SH-01-P1--SH-01-P2", "SH-02-P1 -SH-02-P2", "SEALER"):
            key = next(layout["layout_key"] for layout in self.dataset["layouts"] if layout["machine_code"] == machine)
            self.assertEqual(by_layout[key], {"1", "2"})

    def test_05_slot_codes_geometry_and_overlap_are_valid(self) -> None:
        layouts = {item["layout_key"] for item in self.dataset["layouts"]}
        for layout_key in layouts:
            slots = [item for item in self.dataset["layout_slots"] if item["layout_key"] == layout_key]
            codes = [item["code"] for item in slots]
            self.assertEqual(len(codes), len(set(codes)))
            for slot in slots:
                self.assertGreaterEqual(slot["ui_x"], 0.0)
                self.assertGreaterEqual(slot["ui_y"], 0.0)
                self.assertLessEqual(slot["ui_x"] + slot["ui_width"], 1.0001)
                self.assertLessEqual(slot["ui_y"] + slot["ui_height"], 1.0001)
            for index, left in enumerate(slots):
                for right in slots[index + 1:]:
                    self.assertFalse(rectangles_overlap(left, right), (layout_key, left["code"], right["code"]))

    def test_06_no_robot_map_route_or_rds_coordinates_are_generated(self) -> None:
        forbidden_keys = {
            "robot_x", "robot_y", "map_x", "map_y", "waypoint", "route_id",
            "navigation_profile_id", "rds_worksite_id", "site_id", "stitch_config",
        }

        def walk(value: object) -> None:
            if isinstance(value, dict):
                self.assertFalse(forbidden_keys.intersection(value))
                for child in value.values():
                    walk(child)
            elif isinstance(value, list):
                for child in value:
                    walk(child)

        walk(self.dataset)

    def test_07_unknown_physical_dimensions_remain_null(self) -> None:
        self.assertTrue(all(item["metadata"]["physical_footprint_mm"] is None for item in self.dataset["layout_slots"]))
        self.assertEqual(self.dataset["counts"]["unknown_physical_footprints"], len(self.dataset["layout_slots"]))

    def test_08_unknown_parts_do_not_create_master_data(self) -> None:
        canonical = {item["part_code"] for item in self.phase02a["parts"]}
        resolved = {
            code
            for slot in self.dataset["layout_slots"]
            for code in slot["metadata"].get("resolved_part_codes", [])
        }
        self.assertTrue(resolved <= canonical)
        seed = (PROJECT_ROOT / "db/seeds/generated/002b_customer_physical_layouts.sql").read_text(encoding="utf-8")
        self.assertNotIn("INSERT INTO master.parts", seed)
        self.assertNotIn("INSERT INTO master.container_types", seed)

    def test_09_provenance_exists_for_every_layout_and_slot(self) -> None:
        for item in self.dataset["layouts"]:
            self.assertTrue(all(item["source"].get(key) for key in ("file", "sheet", "range", "workbook_sha256")))
        for item in self.dataset["layout_slots"]:
            source = item["metadata"]["source"]
            self.assertTrue(all(source.get(key) for key in ("file", "sheet", "range", "workbook_sha256")))

    def test_10_customer_sources_remain_byte_identical(self) -> None:
        self.assertTrue(self.dataset["source_files_unchanged"])
        self.assertEqual(self.manifest_before, build_manifest(PROJECT_ROOT))

    def test_11_review_image_exists_for_each_layout(self) -> None:
        review_files = list((PROJECT_ROOT / "data/generated/phase_02b/review").glob("*.png"))
        self.assertEqual(len(review_files), len(self.dataset["layouts"]))
        self.assertTrue(all(path.stat().st_size > 10_000 for path in review_files))

    def test_12_haos_hmtr_discrepancy_and_sealer_gap_are_reported(self) -> None:
        issues = self.dataset["data_issues"]
        discrepancies = [item for item in issues if item["code"] == "PART_CODE_SOURCE_DISCREPANCY"]
        self.assertEqual(len(discrepancies), 2)
        self.assertTrue(all(item["canonical_part_code"] == "67115BN050-10-HAOS" for item in discrepancies))
        self.assertTrue(any(item["code"] == "SEALER_VARIANT_3_LAYOUT_UNPROVEN" for item in issues))

    def test_13_discovered_counts_are_stable(self) -> None:
        expected = json.loads((Path(__file__).parent / "expected_counts.json").read_text(encoding="utf-8"))
        for key, value in expected.items():
            self.assertEqual(self.dataset["counts"][key], value, key)


if __name__ == "__main__":
    unittest.main(verbosity=2)
