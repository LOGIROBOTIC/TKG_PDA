"""Builds docs/TKG_PDA_COMPLETE_TECHNICAL_REFERENCE.{md,pdf}.

Reads the inventory and the extracted fact files, assembles the block model from
the content modules, verifies file coverage, and renders both outputs.
"""

import datetime
import io
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import content_data
import content_files
import content_main
import content_ref
import content_rds
import content_ui
import doc_model

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DOCS = os.path.join(ROOT, "docs")


def load_inventory():
    rows = []
    with io.open(os.path.join(DOCS, "source_inventory.txt"), encoding="utf-8") as fh:
        for line in fh:
            if line.startswith("#") or not line.strip():
                continue
            parts = line.rstrip("\n").split("\t")
            if len(parts) != 3:
                continue
            rows.append((parts[0], parts[1], parts[2]))
    return rows


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True,
                          text=True, encoding="utf-8", check=True).stdout.strip()


def main():
    inventory = load_inventory()
    counts = {}
    for cat, _, _ in inventory:
        counts[cat] = counts.get(cat, 0) + 1

    branch = git("rev-parse", "--abbrev-ref", "HEAD")
    commit = git("rev-parse", "HEAD")
    try:
        base = git("rev-parse", "--short", "main")
    except Exception:
        base = "-"
    date = datetime.date.today().isoformat()

    # Section 27 is built first so coverage can be verified before rendering.
    files_blocks, documented_count, documented_paths = content_files.section_files(inventory)
    inventory_documented = sorted(p for cat, p, _ in inventory if cat == "DOCUMENTED")
    missing = [p for p in inventory_documented if p not in set(documented_paths)]
    sections = len(documented_paths)
    coverage_pct = 0.0 if not inventory_documented else 100.0 * sections / len(inventory_documented)
    coverage = f"{coverage_pct:.1f}%".replace(".0%", "%")

    stats = {
        "documented": counts.get("DOCUMENTED", 0),
        "generated": counts.get("GENERATED", 0),
        "dependency": counts.get("DEPENDENCY", 0),
        "build_output": counts.get("BUILD_OUTPUT", 0),
        "irrelevant": counts.get("IRRELEVANT_ARTIFACT", 0),
        "total": len(inventory),
        "sections": sections,
        "coverage": coverage,
        "missing": missing,
    }

    coverage_line = (
        f"Bulunan ilgili kaynak dosya: {stats['documented']} / "
        f"Dokümante edilen: {sections} / Kapsam: {coverage}"
    )

    meta = {
        "title": "TKG_PDA Teknik Referans",
        "subtitle": "Kaynak kod ve sistem mantığının tam teknik referansı",
        "branch": branch,
        "commit": commit,
        "base": base,
        "date": date,
        "coverage_line": coverage_line,
        "footer": f"TKG_PDA · {branch} · {date}",
        "cover_note": (
            "Bu doküman yukarıda belirtilen commit'teki kaynak koddan üretilmiştir. "
            "Hiçbir gerçek sır içermez; bağlantı dizeleri ve anahtarlar yer tutucularla "
            "(<DB_CONNECTION>, <JWT_SIGNING_KEY>, <INTEGRATION_KEY>, <CALLBACK_KEY>) anılır. "
            "Sahada doğrulanması gereken değerler COMMISSIONING VALIDATION REQUIRED olarak işaretlidir."
        ),
    }

    blocks = []
    blocks += content_main.section_about(meta)
    blocks += content_main.section_overview()
    blocks += content_main.section_architecture()
    blocks += content_main.section_stack()
    blocks += content_main.section_repo()
    blocks += content_main.section_domain()
    blocks += content_main.section_lifecycle()
    blocks += content_rds.section_container_pipeline()
    blocks += content_rds.section_rds_arch()
    blocks += content_rds.section_mutation_policy()
    blocks += content_rds.section_identity()
    blocks += content_rds.section_lease()
    blocks += content_rds.section_alarms()
    blocks += content_rds.section_mapswitch()
    blocks += content_rds.section_dispatch_flow()
    blocks += content_rds.section_reconcile()
    blocks += content_data.section_auth()
    blocks += content_data.section_schema()
    blocks += content_data.section_routing()
    blocks += content_data.section_api()
    blocks += content_ui.section_pda()
    blocks += content_ui.section_adminweb()
    blocks += content_data.section_config()
    blocks += content_ui.section_tests()
    blocks += content_ui.section_tooling()
    blocks += content_ui.section_commissioning()
    blocks += [("pagebreak",)]
    blocks += files_blocks
    blocks += [("pagebreak",)]
    blocks += content_ref.section_callgraphs()
    blocks += content_ref.section_dbflows()
    blocks += content_ref.section_errorcodes()
    blocks += content_ref.section_troubleshooting()
    blocks += content_ref.section_security()
    blocks += content_ref.section_glossary()
    blocks += content_ref.section_coverage(stats)
    blocks += content_ref.section_fileindex(inventory)

    md_path = os.path.join(DOCS, "TKG_PDA_COMPLETE_TECHNICAL_REFERENCE.md")
    pdf_path = os.path.join(DOCS, "TKG_PDA_COMPLETE_TECHNICAL_REFERENCE.pdf")

    with io.open(md_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(doc_model.render_markdown(blocks, meta))

    pages = doc_model.render_pdf(blocks, meta, pdf_path)

    print("branch          :", branch)
    print("commit          :", commit)
    print("blocks          :", len(blocks))
    print("documented files:", stats["documented"])
    print("file sections   :", sections)
    print("coverage        :", coverage)
    if missing:
        print("MISSING:")
        for m in missing:
            print("  ", m)
    print("markdown        :", md_path, os.path.getsize(md_path), "bytes")
    print("pdf             :", pdf_path, os.path.getsize(pdf_path), "bytes")
    print("pdf pages       :", pages)
    return 0 if not missing else 1


if __name__ == "__main__":
    sys.exit(main())
