"""Check task boundaries and pinned evidence without building or changing shared state."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[4]
EVIDENCE = Path(__file__).resolve().parent
PREFIXES = (
    "src/CP6.Platform.AspNetCore/HttpV2/",
    "tests/CP6.Platform.AspNetCoreTests/HttpV2/",
    "docs/development/evidence/WP02-11/",
)
EXACT = {
    "src/CP6.Platform.AspNetCore/Cp6HttpResilienceServiceCollectionExtensions.cs",
    "src/CP6.Platform.AspNetCore/Cp6ProblemDetailsExtensions.cs",
    "docs/WP02-11-HTTP-V2.md",
}


def git(*args: str) -> bytes:
    return subprocess.check_output(
        ["git", "-c", "core.longpaths=true", *args], cwd=ROOT
    )


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_json(name: str):
    return json.loads((EVIDENCE / name).read_text(encoding="utf-8-sig"))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    errors: list[str] = []
    sources = read_json("sources.json")
    baseline = read_json("legacy-baseline.json")
    verified_assets = []
    for asset in sources["assets"]:
        path = ROOT / asset["path"]
        actual = digest(path.read_bytes())
        if actual != asset["sha256"]:
            errors.append(f"Pinned input changed: {asset['path']}")
        verified_assets.append({"path": asset["path"], "sha256": actual})

    unchanged = []
    for item in baseline["files"]:
        original = git("show", f"{baseline['baseline']}:{item['path']}")
        if digest(original) != item["git_bytes_sha256"]:
            errors.append(f"Baseline pin mismatch: {item['path']}")
        if item["permitted_change"] == "READ_ONLY":
            current = (ROOT / item["path"]).read_bytes()
            # Git text checkout may contain CRLF; the source identity is LF-normalized.
            same = current.replace(b"\r\n", b"\n") == original.replace(b"\r\n", b"\n")
            if not same:
                errors.append(f"Read-only input changed: {item['path']}")
            unchanged.append({"path": item["path"], "unchanged": same})

    paths: set[str] = set()
    for command in (
        ("diff", "--name-only", "-z", f"{baseline['baseline']}...HEAD"),
        ("diff", "--name-only", "-z", "HEAD"),
        ("ls-files", "--others", "--exclude-standard", "-z"),
    ):
        paths.update(p.decode("utf-8") for p in git(*command).split(b"\0") if p)
    for path in sorted(paths):
        if path not in EXACT and not path.startswith(PREFIXES):
            errors.append(f"Outside assigned write window: {path}")
        if "__pycache__" in Path(path).parts:
            errors.append(f"Generated Python cache is not delivery evidence: {path}")

    scope = read_json("ac-scope.json")
    foundation = read_json("inputs/foundation.json")
    # Locate original AC objects recursively without relying on catalog layout.
    original_cases = {}

    def collect(value):
        if isinstance(value, dict):
            if str(value.get("id", "")).startswith("PLT-HTTP-01::"):
                original_cases[value["id"]] = value
            for child in value.values():
                collect(child)
        elif isinstance(value, list):
            for child in value:
                collect(child)

    collect(foundation)
    mapped = scope["cases"]
    ids = [row["original"]["id"] for row in mapped]
    if len(ids) != 42 or len(set(ids)) != 42 or set(ids) != set(original_cases):
        errors.append("Original AC map must contain exactly all 42 distinct source identities")
    for row in mapped:
        original = row["original"]
        if original != original_cases.get(original["id"]):
            errors.append(f"Original AC content was altered: {original['id']}")
        if row["business_acceptance"] != "NOT_RUN":
            errors.append(f"Library evidence cannot sign off business acceptance: {original['id']}")

    result = {
        "task": "WP02-11",
        "baseline": baseline["baseline"],
        "head_at_check": git("rev-parse", "HEAD").decode().strip(),
        "pinned_assets": verified_assets,
        "read_only_inputs": unchanged,
        "changed_paths": sorted(paths),
        "original_ac_count": len(ids),
        "business_acceptance": "NOT_RUN",
        "result": "PASS" if not errors else "FAIL",
        "errors": errors,
        "limits": "Scope/provenance check only; not compilation, behavior, additive-overload review, or real Owner acceptance.",
    }
    output = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(output, encoding="utf-8", newline="\n")
    print(output)
    raise SystemExit(1 if errors else 0)


if __name__ == "__main__":
    main()
