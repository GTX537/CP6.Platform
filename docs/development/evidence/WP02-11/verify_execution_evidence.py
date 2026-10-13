"""Verify current disk inputs/artifacts against one execution manifest and parse actual TRX gates.

This does not rerun tests or claim unrelated gate executions used this manifest.
Pass only TRX runs whose inputs are bound by the selected manifest.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("trx", nargs="+", type=Path)
    args = parser.parse_args()
    raw = (ROOT / args.manifest).read_bytes()
    manifest = json.loads(raw.decode("utf-8-sig"))
    errors = []
    counts = {}
    for kind in ("source", "restoredDependencies", "compiledArtifacts"):
        counts[kind] = len(manifest[kind])
        for row in manifest[kind]:
            target = ROOT / row["path"]
            if not target.is_file() or hashlib.sha256(target.read_bytes()).hexdigest() != row["sha256"]:
                errors.append(f"{kind}: {row['path']}")
    gates = []
    for path in args.trx:
        data = (ROOT / path).read_bytes()
        tree = ET.fromstring(data)
        counts_node = tree.find(".//t:Counters", NS)
        counters = counts_node.attrib
        actual = tree.findall(".//t:UnitTestResult", NS)
        if int(counters["total"]) == 0 or counters["passed"] != counters["total"]:
            errors.append(f"{path}: empty or not fully passed")
        if len(actual) != int(counters["total"]) or any(r.attrib["outcome"] != "Passed" for r in actual):
            errors.append(f"{path}: actual test results disagree with counters")
        gates.append({"path": path.as_posix(), "counters": counters, "sha256": hashlib.sha256(data).hexdigest()})
    result = {
        "verified_at_utc": datetime.now(timezone.utc).isoformat(),
        "manifest": args.manifest.as_posix(),
        "manifest_sha256": hashlib.sha256(raw).hexdigest(),
        "input_counts": counts,
        "gates": gates,
        "errors": errors,
        "result": "PASS" if not errors else "FAIL",
        "scope": "Independent current disk and retained execution evidence check; not test execution or business acceptance.",
    }
    (ROOT / args.output).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"result": result["result"], "input_counts": counts, "gate_counts": [g["counters"]["total"] for g in gates], "errors": errors}))
    raise SystemExit(bool(errors))


if __name__ == "__main__":
    main()
