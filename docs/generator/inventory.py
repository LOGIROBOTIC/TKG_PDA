"""TKG_PDA source inventory and structural fact extractor.

Walks the tracked file list, classifies every path into exactly one category and
extracts real structural facts (types, methods, imports) from each documented
source file. Nothing here is invented: everything written to the JSON comes from
reading the file on disk at the documented commit.

Outputs:
  docs/source_inventory.txt   human/machine readable classification, one line per file
  docs/generator/facts.json       structural facts for the documented files
"""

import io
import json
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

CAT_DOC = "DOCUMENTED"
CAT_GEN = "GENERATED"
CAT_DEP = "DEPENDENCY"
CAT_BUILD = "BUILD_OUTPUT"
CAT_ART = "IRRELEVANT_ARTIFACT"

# Platform scaffolding produced by `flutter create` / tooling. Runtime-relevant
# only in the sense that it must exist; it carries no TKG business logic.
PLATFORM_PREFIXES = (
    "src/TkgPda.Pda/android/",
    "src/TkgPda.Pda/ios/",
    "src/TkgPda.Pda/linux/",
    "src/TkgPda.Pda/macos/",
    "src/TkgPda.Pda/windows/",
    "src/TkgPda.Pda/web/",
)

BUILD_MARKERS = ("/bin/", "/obj/", "/build/", "/.dart_tool/", "/coverage/", "/dist/")
DEP_MARKERS = ("node_modules/",)

ART_EXT = {".png", ".jpg", ".jpeg", ".gif", ".ico", ".woff2", ".ttf", ".svg", ".docx", ".pdf"}
DOC_EXT = {
    ".cs", ".dart", ".ts", ".tsx", ".js", ".mjs", ".css", ".html",
    ".sql", ".ps1", ".sh", ".cmd", ".bat", ".py",
    ".json", ".csproj", ".slnx", ".props", ".yaml", ".yml", ".kts", ".xml",
    ".md", ".txt", ".properties",
}


def classify(path: str) -> tuple[str, str]:
    """Returns (category, reason)."""
    low = path.lower()
    ext = os.path.splitext(low)[1]

    if any(m in "/" + low for m in DEP_MARKERS):
        return CAT_DEP, "third-party dependency tree"
    if any(m in "/" + low for m in BUILD_MARKERS):
        return CAT_BUILD, "compiler/bundler output"
    if low.endswith("package-lock.json") or low.endswith("pubspec.lock"):
        return CAT_DEP, "dependency lock file"
    if low.startswith("rds dökümanlari/") or low.startswith("rds dökümanları/"):
        return CAT_ART, "vendor documentation, untracked reference set"
    if path.startswith(PLATFORM_PREFIXES):
        return CAT_ART, "Flutter platform scaffolding, no TKG logic"
    if ext in ART_EXT:
        return CAT_ART, "binary/media asset"
    if low.startswith("data/generated/"):
        return CAT_GEN, "generated customer data extract"
    # Generated seed SQL is still documented: it is executed against real
    # databases, so section 24 requires it to be described and marked generated.
    if low.startswith("db/seeds/generated/") and ext == ".sql":
        return CAT_DOC, "generated seed SQL, documented and marked as generated"
    if low.startswith("db/seeds/generated/"):
        return CAT_GEN, "generated seed artifact"
    # Ignore rules are behaviourally significant here: launchSettings.json is
    # git-ignored, which is why a commissioning launch profile cannot be shared.
    if os.path.basename(low) in (".gitignore", ".gitattributes"):
        return CAT_DOC, "repository behaviour configuration"
    if low.endswith(".g.dart") or low.endswith(".freezed.dart"):
        return CAT_GEN, "code generator output"
    if ext in DOC_EXT:
        return CAT_DOC, "project source/config/script/documentation"
    return CAT_ART, "unclassified non-source artifact"


# --------------------------------------------------------------------------
# Structural extraction. Deliberately conservative regexes: they under-report
# rather than invent. Anything uncertain is simply omitted.
# --------------------------------------------------------------------------

CS_TYPE = re.compile(
    r"^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+)*"
    r"(class|record|interface|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)",
    re.M,
)
CS_METHOD = re.compile(
    r"^\s*(?:public|internal|private|protected)\s+(?:static\s+|async\s+|virtual\s+|override\s+|sealed\s+)*"
    r"(?:[A-Za-z_][\w<>,\[\]\?\. ]*?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
    re.M,
)
CS_USING = re.compile(r"^\s*using\s+([A-Za-z_][\w\.]*)\s*;", re.M)

DART_TYPE = re.compile(r"^\s*(?:abstract\s+|sealed\s+)?(class|enum|mixin)\s+([A-Za-z_]\w*)", re.M)
DART_METHOD = re.compile(
    r"^\s{2,}(?:static\s+|Future<[^>]*>\s+|void\s+|bool\s+|String\s+|int\s+|double\s+)"
    r"([a-z_]\w*)\s*\(",
    re.M,
)
DART_IMPORT = re.compile(r"^\s*import\s+'([^']+)'", re.M)

TS_TYPE = re.compile(r"^\s*export\s+(?:default\s+)?(?:interface|type|class|enum)\s+([A-Za-z_]\w*)", re.M)
TS_FUNC = re.compile(r"^\s*export\s+(?:async\s+)?(?:function|const)\s+([A-Za-z_]\w*)", re.M)
TS_IMPORT = re.compile(r"^\s*import\s+.*?from\s+'([^']+)'", re.M)
TS_LOCAL = re.compile(r"^\s*(?:const|function)\s+([A-Za-z_]\w*)\s*(?:=\s*(?:async\s*)?\(|\()", re.M)
TS_API = re.compile(r"\bapi\.([A-Za-z_]\w*)\s*\(", re.M)
TS_HOOK = re.compile(r"\b(useState|useEffect|useMemo|useQuery|useMutation|useCallback|useRef|useParams|useNavigate)\b")

SQL_OBJ = re.compile(r"(?i)\bCREATE\s+(?:OR\s+REPLACE\s+)?(TABLE|INDEX|UNIQUE\s+INDEX|VIEW|FUNCTION|TRIGGER|SCHEMA|TYPE)\s+(?:IF\s+NOT\s+EXISTS\s+)?([A-Za-z_][\w\.\"]*)")
PS_PARAM = re.compile(r"^\s*\[(?:string|int|switch|bool)\]\s*\$([A-Za-z_]\w*)", re.M)


def extract(path: str, text: str) -> dict:
    ext = os.path.splitext(path)[1].lower()
    facts: dict = {"loc": text.count("\n") + 1}

    if ext == ".cs":
        types = CS_TYPE.findall(text)
        facts["types"] = [f"{k} {n}" for k, n in types][:40]
        # Positional records match the method regex on their primary constructor;
        # subtracting the declared type names keeps the method list honest.
        type_names = {n for _, n in types}
        skip = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return", "get", "set"}
        facts["methods"] = [
            m for m in dict.fromkeys(CS_METHOD.findall(text)) if m not in skip and m not in type_names
        ][:40]
        facts["imports"] = [u for u in dict.fromkeys(CS_USING.findall(text)) if u.startswith("TkgPda")][:20]
    elif ext == ".dart":
        facts["types"] = [f"{k} {n}" for k, n in DART_TYPE.findall(text)][:40]
        facts["methods"] = list(dict.fromkeys(DART_METHOD.findall(text)))[:40]
        facts["imports"] = [i for i in dict.fromkeys(DART_IMPORT.findall(text)) if not i.startswith("dart:")][:20]
    elif ext in (".ts", ".tsx"):
        facts["types"] = list(dict.fromkeys(TS_TYPE.findall(text)))[:40]
        methods = list(dict.fromkeys(TS_FUNC.findall(text)))
        # Local component helpers carry most of the logic in these pages.
        methods += [m for m in dict.fromkeys(TS_LOCAL.findall(text)) if m not in methods]
        facts["methods"] = methods[:40]
        facts["imports"] = [i for i in dict.fromkeys(TS_IMPORT.findall(text)) if i.startswith(".")][:20]
        facts["api_calls"] = list(dict.fromkeys(TS_API.findall(text)))[:25]
        hooks = dict.fromkeys(TS_HOOK.findall(text))
        if hooks:
            facts["hooks"] = list(hooks)[:15]
    elif ext == ".sql":
        facts["objects"] = [f"{k.upper()} {n}" for k, n in SQL_OBJ.findall(text)][:60]
    elif ext == ".ps1":
        facts["params"] = list(dict.fromkeys(PS_PARAM.findall(text)))[:20]
    return facts


def main() -> int:
    out = subprocess.run(
        ["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", check=True
    )
    files = [f for f in out.stdout.splitlines() if f.strip()]

    rows = []
    facts_map = {}
    for rel in sorted(files):
        cat, reason = classify(rel)
        rows.append((rel, cat, reason))
        if cat != CAT_DOC:
            continue
        abspath = os.path.join(ROOT, rel)
        try:
            with io.open(abspath, encoding="utf-8", errors="replace") as fh:
                text = fh.read()
        except OSError:
            continue
        facts_map[rel] = extract(rel, text)

    inv = os.path.join(ROOT, "docs", "source_inventory.txt")
    with io.open(inv, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("# TKG_PDA source inventory\n")
        fh.write("# format: <CATEGORY>\\t<path>\\t<reason>\n")
        fh.write("# categories: DOCUMENTED GENERATED DEPENDENCY BUILD_OUTPUT IRRELEVANT_ARTIFACT\n")
        for rel, cat, reason in rows:
            fh.write(f"{cat}\t{rel}\t{reason}\n")

    with io.open(os.path.join(ROOT, "docs", "generator", "facts.json"), "w", encoding="utf-8") as fh:
        json.dump(facts_map, fh, ensure_ascii=False, indent=1, sort_keys=True)

    counts: dict[str, int] = {}
    for _, cat, _ in rows:
        counts[cat] = counts.get(cat, 0) + 1
    print("total tracked files:", len(rows))
    for k in sorted(counts):
        print(f"  {k:22} {counts[k]}")
    print("facts extracted for:", len(facts_map))
    return 0


if __name__ == "__main__":
    sys.exit(main())
