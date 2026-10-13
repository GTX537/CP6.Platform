"""Task-local checks only: no publish, signing, workflow or deployment command."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
IDS = ["Abstractions", "AspNetCore", "Contracts", "Deployment", "EntityFramework", "Messaging", "Release"]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", *args], cwd=ROOT).decode().strip()


def snapshot():
    paths = git("ls-files", "--cached", "--others", "--exclude-standard", "--", "src", "tools/CP6.Platform.ReleaseTool",
                "tests/CP6.Platform.ReleaseTests", "tests/CP6.Platform.ArchitectureTests", "contracts", "eng/wp02-27",
                "eng/p10/trust", "Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.config")
    return {p: digest(ROOT / p) for p in sorted(set(paths.splitlines())) if (ROOT / p).is_file()}


class Run:
    def __init__(self, args):
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", args.run_id):
            raise ValueError("Invalid task run ID")
        self.output = ROOT / "artifacts/wp02-27" / args.run_id
        self.output.mkdir(parents=True, exist_ok=False)
        self.env = dict(os.environ)
        self.dotnet = str(Path(args.dotnet).resolve())
        self.env["DOTNET_ROOT"] = str(Path(self.dotnet).parent)
        self.env["PATH"] = self.env["DOTNET_ROOT"] + os.pathsep + self.env.get("PATH", "")
        self.record = {"task": "WP02-27", "action": args.action, "run_id": args.run_id,
                       "started_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                       "source_head": git("rev-parse", "HEAD"), "source_working_bytes": snapshot(),
                       "dotnet": self.dotnet, "commands": [], "scope": "Local validation only; no publication/host/business adoption"}
        self.record["dotnet_sdk"] = subprocess.check_output([self.dotnet, "--version"], cwd=ROOT, env=self.env).decode().strip()

    def command(self, name, args, expected=0, timeout=600):
        log = self.output / (name + ".log")
        argv = [self.dotnet, *map(str, args)]
        started = time.monotonic()
        print("Running " + name, flush=True)
        code = None
        with log.open("xb") as stream:
            try:
                code = subprocess.run(argv, cwd=ROOT, env=self.env, stdout=stream, stderr=subprocess.STDOUT, timeout=timeout).returncode
            except subprocess.TimeoutExpired:
                stream.write(b"\nTASK_RUNNER: local command timed out; no successful result inferred.\n")
        self.record["commands"].append({"name": name, "argv": argv, "exit_code": code,
                                        "expected_exit_code": expected, "elapsed_seconds": round(time.monotonic() - started, 3),
                                        "log": log.name, "log_sha256": digest(log)})
        if code != expected:
            raise RuntimeError(f"{name}: exit {code}, expected {expected}; see {log}")

    def test(self, name, project, filter_text=None):
        self.command(name + "-restore", ["restore", project, "--locked-mode"])
        args = ["test", project, "-c", "Release", "--no-restore", "--logger", f"trx;LogFileName={name}.trx",
                "--results-directory", str(self.output)]
        if filter_text:
            args += ["--filter", filter_text]
        failure = None
        try:
            self.command(name, args)
        except RuntimeError as exc:
            failure = exc
        trx = self.output / (name + ".trx")
        if not trx.is_file():
            raise failure or RuntimeError(f"{name}: no actual TRX result")
        result = ET.parse(trx).find(".//{*}Counters").attrib
        self.record.setdefault("test_results", {})[name] = result
        if failure:
            raise failure
        assert int(result["total"]) > 0 and int(result["failed"]) == 0
        assert int(result["passed"]) == int(result["total"]) == int(result["executed"])
        assert int(result["notExecuted"]) == 0

    def finish(self, error=None):
        self.record["finished_utc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        self.record["source_after"] = snapshot()
        self.record["source_inputs_unchanged"] = self.record["source_working_bytes"] == self.record["source_after"]
        if not self.record["source_inputs_unchanged"]:
            error = error or "Source inputs changed during this run; results require reconciliation"
        self.record["result"] = "PASS" if error is None else "FAIL"
        self.record["error"] = error
        self.record["artifacts"] = {str(p.relative_to(ROOT)).replace("\\", "/"): digest(p)
                                    for p in sorted(self.output.rglob("*")) if p.is_file()}
        self.record["tool_binaries"] = {str(p.relative_to(ROOT)).replace("\\", "/"): digest(p)
                                        for p in sorted((ROOT / "tools/CP6.Platform.ReleaseTool/bin/Release/net8.0").glob("*.dll"))}
        (self.output / "run.json").write_text(json.dumps(self.record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        print(json.dumps({"result": self.record["result"], "output": str(self.output),
                          "source_inputs_unchanged": self.record["source_inputs_unchanged"], "error": error}, ensure_ascii=True), flush=True)
        return error


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["gates", "release", "architecture", "formal", "pack"])
    parser.add_argument("run_id")
    parser.add_argument("--inventory-sha256")
    parser.add_argument("--dotnet", default=shutil.which("dotnet"), required=not bool(shutil.which("dotnet")))
    args = parser.parse_args()
    run = Run(args)
    error = None
    try:
        if args.action in ("gates", "release", "architecture"):
            failures = []
            checks = [
                ("release", "tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj",
                 "FullyQualifiedName~CapabilitiesV1|FullyQualifiedName~FormalPackagePublicationTests|FullyQualifiedName~PinnedNuGetTrustPolicyTests|FullyQualifiedName~FormalPackageVerifierTests"),
                ("architecture", "tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj", None)]
            for name, project, filter_text in checks:
                if args.action != "gates" and name != args.action:
                    continue
                try:
                    run.test(name, project, filter_text)
                except Exception as exc:
                    failures.append(str(exc))
            if failures:
                raise RuntimeError("; ".join(failures))
        elif args.action == "formal":
            inventory = ROOT / "docs/development/evidence/WP02-27/inputs.json"
            assert args.inventory_sha256 == digest(inventory), "Supply independently captured inventory hash"
            run.command("inspector-build", ["build", "eng/wp02-27/Inspector/Inspector.csproj", "-c", "Release", "--no-restore"])
            inspector = ROOT / "eng/wp02-27/Inspector/bin/Release/net8.0/Inspector.dll"
            run.record["inspector_sha256"] = digest(inspector)
            utc = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
            run.command("formal-current", [inspector, "formal", ROOT, inventory, args.inventory_sha256,
                        run.output / "inspection", run.dotnet,
                        ROOT / "tools/CP6.Platform.ReleaseTool/bin/Release/net8.0/CP6.Platform.ReleaseTool.dll", utc])
        elif args.action == "pack":
            # All seven are necessary to exercise the exact common-source/version set.
            # Never invoke the P10 formal/test publication helpers or use a stable slot.
            assert not git("status", "--porcelain", "--", "src", "contracts", "eng/wp02-27/Inspector"), "Commit pack inputs first"
            source = git("rev-parse", "HEAD")
            attempt = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d%H%M%S")
            version = f"0.10.3-wp02.27.g{source}.{attempt}"
            run.record.update(package_version=version, package_source=source, publication_status="NOT_PUBLISHED", trust_status="NOT_FORMALLY_TRUSTED")
            for name in IDS:
                project = f"src/CP6.Platform.{name}/CP6.Platform.{name}.csproj"
                run.command(name + "-restore", ["restore", project, "--locked-mode"])
                run.command(name + "-pack", ["pack", project, "-c", "Release", "--no-restore",
                            "-p:PackageVersion=" + version, "-p:RepositoryCommit=" + source,
                            "-p:ContinuousIntegrationBuild=true", "-p:IncludeSymbols=false", "--output", run.output / "packages"])
            inspector = ROOT / "eng/wp02-27/Inspector/bin/Release/net8.0/Inspector.dll"
            run.command("inspector-build", ["build", "eng/wp02-27/Inspector/Inspector.csproj", "-c", "Release", "--no-restore"])
            run.record["inspector_sha256"] = digest(inspector)
            run.command("local-inspection", [inspector, "local", ROOT, run.output / "packages", source, version, run.output / "inspection"])
    except Exception as exc:
        error = str(exc)
    finally:
        error = run.finish(error)
    return int(error is not None)


if __name__ == "__main__":
    sys.exit(main())
