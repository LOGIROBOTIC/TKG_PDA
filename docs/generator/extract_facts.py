"""Extracts the factual backbone the reference document is built from.

Everything here is read off the source tree; nothing is hand-maintained.

Outputs (next to this script):
  endpoints.json    [category, METHOD, path, handler] for every registered route
  schema.json       {schema: [tables]} from db/migrations
  errorcodes.json   {code: {type, msg, files}} for every error code in the source
"""

import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

# --------------------------------------------------------------------------
# HTTP endpoints
# --------------------------------------------------------------------------

# The handler tail is matched with a lookahead so it is not consumed: a greedy
# consuming tail swallows the start of the next registration and silently drops it.
MAP = re.compile(r"\.Map(Get|Post|Put|Delete|Patch)\(\s*\"([^\"]+)\"\s*,\s*(?=(.{0,90}))", re.S)
# A method-group handler is a bare identifier closing the Map(...) call; anything
# else (an inline lambda's parameter list) is reported as such rather than guessed.
METHOD_GROUP = re.compile(r"^([A-Za-z_][\w\.]*)\s*\)")
MAP_GROUP = re.compile(r"\.MapGroup\(\s*\"([^\"]+)\"")

ENDPOINT_FILES = [
    ("Operator/Auth", "src/TkgPda.Api/ApiEndpoints.cs"),
    ("Admin", "src/TkgPda.Api/AdminEndpoints.cs"),
    ("RDS/Internal", "src/TkgPda.Api/RdsIntegrationEndpoints.cs"),
]


def endpoints():
    rows = []
    for category, rel in ENDPOINT_FILES:
        with io.open(os.path.join(ROOT, rel), encoding="utf-8") as fh:
            text = fh.read()
        # Routes registered on a MapGroup carry relative paths; restore the full
        # path so the table matches what a client actually calls.
        groups = MAP_GROUP.findall(text)
        prefix = groups[0] if len(groups) == 1 else ""
        for method, path, tail in MAP.findall(text):
            m = METHOD_GROUP.match(tail.strip())
            handler = m.group(1) if m else "(inline lambda)"
            full = path if path.startswith("/api/") or not prefix else prefix + path
            rows.append([category, method.upper(), full, handler])
    return rows


# --------------------------------------------------------------------------
# Database schema
# --------------------------------------------------------------------------

CREATE_TABLE = re.compile(
    r"(?is)CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?([a-z_]+)\.([a-z_]+)"
)


def schema():
    out = {}
    mig_dir = os.path.join(ROOT, "db", "migrations")
    for name in sorted(os.listdir(mig_dir)):
        if not name.endswith(".sql"):
            continue
        with io.open(os.path.join(mig_dir, name), encoding="utf-8") as fh:
            text = fh.read()
        for sch, table in CREATE_TABLE.findall(text):
            out.setdefault(sch, set()).add(table)
    return {k: sorted(v) for k, v in out.items()}


# --------------------------------------------------------------------------
# Error codes
# --------------------------------------------------------------------------

# throw new SomeException("CODE", "message")  — message may be interpolated
THROWN = re.compile(
    r"throw\s+new\s+([A-Za-z]*Exception)\s*\(\s*"
    r"(?:StatusCodes\.[A-Za-z0-9]+\s*,\s*)?"
    r"\"([A-Z][A-Z0-9_]{3,})\"\s*,\s*[\$@]{0,2}\"([^\"]{0,400})\"",
    re.S,
)
# new SomeException("CODE", ...) without throw (returned or rethrown)
CONSTRUCTED = re.compile(
    r"new\s+([A-Za-z]*Exception)\s*\(\s*"
    r"(?:StatusCodes\.[A-Za-z0-9]+\s*,\s*)?"
    r"\"([A-Z][A-Z0-9_]{3,})\"",
    re.S,
)
# const string X = "CODE";
CONSTANT = re.compile(r"const\s+string\s+\w+\s*=\s*\"([A-Z][A-Z0-9_]{3,})\"")
# WriteExecutionAuditAsync(..., "ACTION", "REASON_CODE", ...)
AUDIT = re.compile(
    r"WriteExecutionAuditAsync\(\s*[^,]+,\s*\"([A-Z][A-Z0-9_]{3,})\"\s*,\s*\"([A-Z][A-Z0-9_]{3,})\""
)
# result records carrying a code as a bare literal in a return position
RESULT = re.compile(r"return\s+new\s*\([^;]{0,400}?\"([A-Z][A-Z0-9_]{4,})\"", re.S)

# Words that look like codes but are HTTP verbs, roles, states or SQL keywords.
NOT_A_CODE = {
    "ADMIN", "SUPERVISOR", "LINE_OPERATOR", "WAREHOUSE_OPERATOR",
    "BRING_EMPTY", "REMOVE_FULL", "REMOVE_EMPTY",
    "VACANT", "OCCUPIED", "UNKNOWN", "EMPTY", "FULL",
    "MACHINE", "STAGING", "EMPTY_STORAGE", "FULL_STORAGE", "WAREHOUSE",
    "PICKUP", "DROP", "SHARED", "OTHER",
    "DEVICE_BOUND", "ADMIN_WEB", "DRAFT", "PUBLISHED", "ARCHIVED",
    "RDSCORE_NATIVE_ORDER", "TKG_TRANSPORT",
    # Plain status words, not codes a caller would branch on.
    "AVAILABLE", "SUCCESS", "FAILED", "READY", "SYNCHRONIZED",
}


def error_codes():
    codes = {}

    def add(code, kind, msg, rel):
        if code in NOT_A_CODE:
            return
        entry = codes.setdefault(code, {"type": kind, "msg": msg, "files": []})
        # A real exception definition always beats a weaker match.
        if entry["type"] in ("audit reason", "constant") and kind.endswith("Exception"):
            entry["type"] = kind
            if msg:
                entry["msg"] = msg
        if not entry["msg"] and msg:
            entry["msg"] = msg
        if rel not in entry["files"]:
            entry["files"].append(rel)

    for base, _dirs, files in os.walk(os.path.join(ROOT, "src")):
        if any(part in base for part in ("bin", "obj", "node_modules")):
            continue
        for name in files:
            if not name.endswith(".cs"):
                continue
            rel = os.path.relpath(os.path.join(base, name), ROOT).replace("\\", "/")
            with io.open(os.path.join(base, name), encoding="utf-8", errors="replace") as fh:
                text = fh.read()
            for kind, code, msg in THROWN.findall(text):
                add(code, kind, " ".join(msg.split()), rel)
            for kind, code in CONSTRUCTED.findall(text):
                add(code, kind, "", rel)
            for code in CONSTANT.findall(text):
                add(code, "constant", "", rel)
            for _action, reason in AUDIT.findall(text):
                add(reason, "audit reason", "execution audit reason", rel)
            for code in RESULT.findall(text):
                add(code, "result code", "", rel)
    return codes


def main():
    eps = endpoints()
    sch = schema()
    err = error_codes()

    for name, data in (("endpoints.json", eps), ("schema.json", sch), ("errorcodes.json", err)):
        with io.open(os.path.join(HERE, name), "w", encoding="utf-8") as fh:
            json.dump(data, fh, ensure_ascii=False, indent=1, sort_keys=not isinstance(data, list))

    print("endpoints:", len(eps))
    by_cat = {}
    for cat, *_ in eps:
        by_cat[cat] = by_cat.get(cat, 0) + 1
    for k in sorted(by_cat):
        print(f"   {k:16} {by_cat[k]}")
    print("schemas  :", len(sch), "tables:", sum(len(v) for v in sch.values()))
    print("codes    :", len(err))
    return 0


if __name__ == "__main__":
    sys.exit(main())
