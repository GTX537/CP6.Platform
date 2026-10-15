"""Read-only WP02-26 push/PR audit; fail closed outside the observed workflow syntax."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
RFC_WORKFLOW = ".github/workflows/p10-rfc3161-preflight.yml"
RFC_TEST = "tests/CP6.Platform.ReleaseTests/P10Rfc3161PreflightWorkflowContractTests.cs"


def git(*args):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", *args], cwd=ROOT)


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise ValueError(f"Refusing to overwrite workflow audit: {args.output}")
    changes = set(git("diff", "--name-only", "origin/main...HEAD").decode().splitlines())
    changes.update(git("diff", "--name-only", "HEAD").decode().splitlines())
    changes.update(git("ls-files", "--others", "--exclude-standard").decode().splitlines())
    if len(changes) > 300:
        raise ValueError("Candidate exceeds 300-file PR filter audit boundary; manual review required")
    records = []
    for ref in ("HEAD", "origin/main"):
        commit = git("rev-parse", ref).decode().strip()
        files = git("ls-tree", "-r", "--name-only", ref, "--", ".github/workflows/").decode().splitlines()
        if not files:
            raise ValueError(f"No workflow inventory at {ref}")
        for path in files:
            raw = git("show", f"{ref}:{path}")
            lines = raw.decode().splitlines()
            starts = [i for i, line in enumerate(lines) if line == "on:"]
            if len(starts) != 1:
                raise ValueError(f"Workflow syntax requires fresh review: {ref}:{path}")
            block = []
            for line in lines[starts[0] + 1:]:
                if line and not line[0].isspace() and not line.startswith("#"):
                    break
                if line.strip():
                    block.append(line)
            events = [m.group(1) for line in block if (m := re.fullmatch(r"  ([a-z_]+):", line))]
            if events == ["workflow_dispatch"] and block[0] == "  workflow_dispatch:" and all(
                line.startswith("    ") for line in block[1:]
            ):
                eligible = []
            elif path == RFC_WORKFLOW and block == [
                "  workflow_dispatch:", "  pull_request:", "    paths:",
                f'      - "{RFC_WORKFLOW}"', f'      - "{RFC_TEST}"',
            ]:
                eligible = sorted(changes.intersection((RFC_WORKFLOW, RFC_TEST)))
            else:
                raise ValueError(f"Unreviewed trigger or filter: {ref}:{path}: {block}")
            if eligible:
                raise ValueError(f"Candidate could trigger RFC3161 PR workflow: {eligible}")
            if ref == "HEAD":
                if (ROOT / path).read_text(encoding="utf-8") != raw.decode().replace("\r\n", "\n"):
                    raise ValueError(f"Working workflow differs: {path}")
            dependencies = [line.strip() for line in lines if re.search(r"\buses:", line)]
            records.append(dict(ref=ref, commit=commit, path=path,
                                sha256=hashlib.sha256(raw).hexdigest(), events=events,
                                trigger_block=block, dependencies=dependencies,
                                matching_candidate_paths=eligible, branch_push_safe=True,
                                ordinary_task_pr_safe=True))
    report = dict(observed_at=datetime.now(timezone.utc).isoformat(),
                  scope="Plain task branch push/PR only; no dispatch/tag/publication authorization",
                  parser="Strict exact-shape audit of all current event blocks; other syntax fails closed",
                  candidate_changes=sorted(changes), workflows=records,
                  actions_triggered=[], actions_rerun=[], actions_cancelled=[], workflow_changes=[])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"PASS: {len(records)} candidate/protected-main workflow entries; narrow RFC3161 PR paths do not match")


if __name__ == "__main__":
    main()
