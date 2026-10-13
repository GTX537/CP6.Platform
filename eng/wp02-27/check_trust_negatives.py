"""Verify task-owned negative copies with the unchanged formal CLI. No signing/publication."""
import argparse
import datetime
import json
from pathlib import Path
import sys
import zipfile

from run_checks import ROOT, Run, digest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run_id")
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--case", choices=["tampered"], help="Only rerun an affected case")
    args = parser.parse_args()
    args.action = "negative-trust"
    run = Run(args)
    error = None
    try:
        inventory = json.loads((ROOT / "docs/development/evidence/WP02-27/inputs.json").read_bytes())
        packages = next(v for v in inventory.values() if isinstance(v, list) and v and "package_id" in v[0])
        original = packages[0]
        archive = Path(original["path"])
        assert digest(archive) == original["sha256"]
        utc = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
        fixtures = run.output / "fixtures"
        inspector = ROOT / "eng/wp02-27/Inspector/bin/Release/net8.0/Inspector.dll"
        tool = ROOT / "tools/CP6.Platform.ReleaseTool/bin/Release/net8.0/CP6.Platform.ReleaseTool.dll"
        run.record.update(evaluation_utc=utc, original_archive_sha256=digest(archive), inspector_sha256=digest(inspector))
        run.command("fixtures", [inspector, "fixtures", ROOT, fixtures, utc, archive])
        tampered = fixtures / "tampered.nupkg"
        with zipfile.ZipFile(archive) as source, zipfile.ZipFile(tampered, "x") as target:
            for entry in source.infolist():
                data = source.read(entry)
                if entry.filename.endswith(".dll"):
                    data = data[:-1] + bytes([data[-1] ^ 1])
                target.writestr(entry, data)
        cases = [
            ("historical-current", "Historical", archive, "Current", 2),
            ("historical-audit", "Historical", archive, "HistoricalAudit", 0),
            ("revoked-current", "Revoked", archive, "Current", 2),
            ("unknown-signer", "UnknownSigner", archive, "Current", 2),
            ("missing-timestamp", "Current", fixtures / "missing-timestamp.nupkg", "Current", 2),
            # Malformed CMS may fail during NuGet decoding, before a contract error exists.
            ("bad-timestamp", "Current", fixtures / "bad-timestamp.nupkg", "Current", 1),
            ("tampered", "Current", tampered, "Current", 1),
        ]
        failures = []
        for name, policy, package, mode, expected in cases:
            if args.case and name != args.case:
                continue
            try:
                run.command(name, [tool, "verify-formal-package", package, fixtures / policy / "policy.json",
                                  fixtures / policy / "certificates", original["package_id"], "0.10.2",
                                  original["source_git_sha"], utc, mode], expected=expected, timeout=240)
            except Exception as exc:
                failures.append(str(exc))
        run.record["original_archive_unchanged"] = digest(archive) == original["sha256"]
        assert run.record["original_archive_unchanged"]
        if failures:
            raise RuntimeError("; ".join(failures))
    except Exception as exc:
        error = str(exc)
    finally:
        error = run.finish(error)
    return int(error is not None)


if __name__ == "__main__":
    sys.exit(main())
