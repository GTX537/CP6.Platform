"""Read-only scope/byte audit for the assigned wave8 registration window."""
from pathlib import Path
import datetime
import hashlib
import json
import subprocess

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "docs/development/evidence/WP02-02/formal-registration"
BASE = "66480fe24d51c706708be971e7e9d75a684a3bc4"
ALLOWED = {
    "src/CP6.Platform.Release/Cp6FormalPackagePublicationValidator.cs",
    "src/CP6.Platform.Release/CapabilitiesV1/Cp6CapabilityValidator.cs",
    "contracts/release/v1/formal-package-publication.v1.schema.json",
    "contracts/release/capabilities/v1/capability-matrix.v1.schema.json",
    ".github/workflows/p10-formal-packages.yml",
    "docs/P10-RELEASE-GOVERNANCE.md",
    *[f"eng/p10/{name}.ps1" for name in (
        "Test-P10FormalPrerequisites", "Pack-P10FormalPackages", "New-P10FormalPackageSet",
        "Publish-P10FormalPackageSet", "New-P10FormalPublicationRecord", "Test-P10FormalPackageSet")],
    *[f"tests/CP6.Platform.ReleaseTests/{name}.cs" for name in (
        "FormalPackagePublicationTests", "ReleaseSchemaParityTests", "ReleaseSchemaAssetTests",
        "P10FormalWorkflowContractTests", "ReleaseSchemaTestData", "P10FormalRegistrationTests",
        "CapabilitiesV1/CapabilityValidationTests", "CapabilitiesV1/CapabilityTrustTests")],
}
PREFIXES = ("docs/development/evidence/WP02-02/formal-registration/", "eng/wp02-02/formal-registration/")


def git(*args):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", "-C", str(ROOT), *args])


def digest(data):
    return hashlib.sha256(data).hexdigest()


def audit():
    changed = set(git("diff", "--name-only", BASE).decode().splitlines())
    changed.update(git("ls-files", "--others", "--exclude-standard").decode().splitlines())
    outside = sorted(p for p in changed if p not in ALLOWED and not p.startswith(PREFIXES))
    readonly = json.loads((OUT / "readonly-baseline.json").read_text())["files"]
    mismatches = [r["path"] for r in readonly if digest((ROOT / r["path"]).read_bytes()) != r["checkoutSha256"]]
    workflows = []
    for p in git("ls-tree", "-r", "--name-only", "origin/main", "--", ".github/workflows").decode().splitlines():
        main = git("show", f"origin/main:{p}")
        candidate = (ROOT / p).read_bytes()
        def event_block(data):
            lines = data.decode().splitlines()
            start = lines.index("on:")
            end = next((i for i in range(start + 1, len(lines)) if lines[i] and not lines[i].startswith(" ")), len(lines))
            return "\n".join(lines[start:end]).strip()
        workflows.append({"path": p, "mainSha256": digest(main), "checkoutSha256": digest(candidate),
                          "mainEvents": event_block(main), "candidateEvents": event_block(candidate)})
    narrow = {".github/workflows/p10-rfc3161-preflight.yml", "tests/CP6.Platform.ReleaseTests/P10Rfc3161PreflightWorkflowContractTests.cs"}
    result = {"observedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "baseline": BASE, "head": git("rev-parse", "HEAD").decode().strip(),
              "protectedMain": git("rev-parse", "origin/main").decode().strip(),
              "changedFileCount": len(changed), "changedPaths": sorted(changed),
              "outsideWindow": outside, "readonlyCheckoutMismatches": mismatches,
              "automaticPrPathsTouched": sorted(changed & narrow), "below300Files": len(changed) < 300,
              "workflows": workflows}
    (OUT / "scope-audit.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if outside or mismatches or changed & narrow or len(changed) >= 300:
        raise SystemExit("Scope/readonly/PR path audit failed; inspect scope-audit.json")
    print(json.dumps({"changedFiles": len(changed), "readonlyChecked": len(readonly), "outside": outside, "readonlyMismatches": mismatches}))


if __name__ == "__main__":
    audit()
