from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(PROJECT_ROOT))

from tools.phase_02a.pipeline import (  # noqa: E402
    build_dataset,
    build_manifest,
    normalize_identifier,
    normalize_label,
    normalize_variant,
    run_pipeline,
)


def directory_hashes(root: Path) -> dict[str, str]:
    return {
        path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


class Phase02APipelineTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest_before = build_manifest(PROJECT_ROOT)
        cls.dataset = build_dataset(PROJECT_ROOT)

    def planning_rows(self, part_code: str) -> list[dict]:
        return [
            row for row in self.dataset["planning_metrics"]
            if row["part_code"] == part_code
        ]

    def test_01_deterministic_normalization(self) -> None:
        self.assertEqual(normalize_identifier(" 67115bn050–10–haos "), "67115BN050-10-HAOS")
        self.assertEqual(normalize_label("Kutu Lotu"), "KUTU LOTU")
        self.assertEqual(normalize_label("ÇEVRİM"), "CEVRIM")
        self.assertEqual(normalize_variant(2.0), "2")

    def test_02_rerun_produces_byte_equivalent_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            first_root = Path(first)
            second_root = Path(second)
            run_pipeline(PROJECT_ROOT, first_root / "output", first_root / "seed.sql")
            run_pipeline(PROJECT_ROOT, second_root / "output", second_root / "seed.sql")
            self.assertEqual(directory_hashes(first_root), directory_hashes(second_root))

    def test_03_blank_planning_metric_is_null(self) -> None:
        row = self.planning_rows("715A2BN400-50")[0]
        self.assertIsNone(row["lot_quantity"])
        self.assertIsNone(row["container_code"])

    def test_04_formula_error_becomes_issue_and_null(self) -> None:
        row = self.planning_rows("715A2BN400-50")[0]
        self.assertIsNone(row["agv_moves_per_day"])
        formula_issues = [
            issue for issue in self.dataset["data_issues"]
            if issue["code"] == "EXCEL_FORMULA_ERROR"
        ]
        self.assertTrue(any(issue["excel_error"] == "#DIV/0!" for issue in formula_issues))

    def test_05_same_part_can_occur_in_multiple_variants(self) -> None:
        rows = self.planning_rows("71515BN400-40")
        contexts = {(row["machine_code"], row["variant_code"]) for row in rows}
        self.assertIn(("SH-02-P1 -SH-02-P2", "1"), contexts)
        self.assertIn(("SH-02-P1 -SH-02-P2", "2"), contexts)

    def test_06_same_part_can_occur_in_multiple_product_groups(self) -> None:
        rows = self.planning_rows("90174-12022")
        groups = {row["main_part_code"] for row in rows}
        self.assertEqual(groups, {"577030D030-10-TES", "577040D030-10-TES"})

    def test_07_haos_hmtr_discrepancy_is_reported(self) -> None:
        issues = [
            issue for issue in self.dataset["data_issues"]
            if issue["code"] == "PART_CODE_SOURCE_DISCREPANCY"
        ]
        self.assertTrue(issues)
        self.assertTrue(all(issue["canonical_part_code"] == "67115BN050-10-HAOS" for issue in issues))
        self.assertNotIn("67115BN050-10-HMTR", {part["part_code"] for part in self.dataset["parts"]})

    def test_08_unknown_container_is_reported_not_guessed(self) -> None:
        row = self.planning_rows("715A2BN400-50")[0]
        self.assertIsNone(row["container_code"])
        self.assertTrue(any(
            issue["code"] == "UNKNOWN_CONTAINER_MAPPING"
            and issue.get("part_code") == "715A2BN400-50"
            for issue in self.dataset["data_issues"]
        ))

    def test_09_customer_sources_remain_unchanged(self) -> None:
        self.assertTrue(self.dataset["source_files_unchanged"])
        self.assertEqual(self.manifest_before, build_manifest(PROJECT_ROOT))

    def test_10_discovered_counts_are_stable(self) -> None:
        expected = json.loads((Path(__file__).parent / "expected_counts.json").read_text(encoding="utf-8"))
        for key, value in expected.items():
            self.assertEqual(self.dataset["counts"][key], value, key)

    def test_11_customer_container_evidence_is_reconciled(self) -> None:
        containers = {item["code"]: item for item in self.dataset["container_types"]}
        self.assertEqual(
            (containers["1284"]["category"], containers["1284"]["length_mm"], containers["1284"]["width_mm"], containers["1284"]["height_mm"]),
            ("CONTAINER", 1200, 800, 400),
        )
        self.assertEqual(
            (containers["BYK"]["length_mm"], containers["BYK"]["width_mm"], containers["BYK"]["height_mm"]),
            (1200, 750, 600),
        )
        self.assertEqual(
            (containers["ORT"]["length_mm"], containers["ORT"]["width_mm"], containers["ORT"]["height_mm"]),
            (950, 650, 550),
        )
        self.assertEqual(
            (containers["KCK"]["length_mm"], containers["KCK"]["width_mm"], containers["KCK"]["height_mm"]),
            (800, 550, 450),
        )
        self.assertEqual(containers["285C"]["category"], "CONTAINER")
        self.assertEqual(containers["ARABA"]["category"], "CART")
        self.assertEqual(self.dataset["unresolved_container_codes"], ["(blank)", "1285"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
