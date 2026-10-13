"""Read-only source/cache inventory. Hash checks are never signature verification."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
MAIN_SHA = "47c9b12c796620effe41c62d16992ccd629ee949"
PLATFORM_SHA = "9259a45d39bae7e98ca804a74778a588c1ad4619"
BODY_SHA = "9246ef6b40f500da22c570bf3c31a51701442bbbed50250d9f259d12cb1bbdbf"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def git(root, *args):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", *args], cwd=root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--main-root", required=True, type=Path)
    args = parser.parse_args()
    admission_path = "docs/development/evidence/wave5-dispatch-20261013/admission-package.json"
    raw = git(args.main_root, "show", f"{MAIN_SHA}:{admission_path}")
    admission = json.loads(raw)
    assert admission["baseline"] == PLATFORM_SHA
    anchors = []
    for item in admission["source_anchors"]:
        content = git(ROOT, "show", f'{PLATFORM_SHA}:{item["path"]}')
        assert sha(content) == item["committed_bytes_sha256"], item["path"]
        assert git(ROOT, "rev-parse", f'{PLATFORM_SHA}:{item["path"]}').decode().strip() == item["git_blob"]
        anchors.append({**item, "verified": True})
    body = args.main_root / "docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md"
    assert sha(body.read_bytes()) == BODY_SHA
    publication_path = "docs/evidence/c01/0.10.2/formal-package-publication.v1.json"
    publication_bytes = (ROOT / publication_path).read_bytes()
    publication = json.loads(publication_bytes)
    records = {p["packageId"]: p for p in publication["packages"]}
    packages = []
    for item in admission["existing_cached_artifacts"]:
        path = Path(item["path"])
        content = path.read_bytes()
        digest = sha(content)
        record = records[item["package_id"]]
        assert digest == item["sha256"] == record["publishedPackageSha256"] == record["authorSignedPackageSha256"]
        with zipfile.ZipFile(path) as archive:
            nuspecs = [n for n in archive.namelist() if n.endswith(".nuspec")]
            assert len(nuspecs) == 1
            metadata = ET.fromstring(archive.read(nuspecs[0])).find("{*}metadata")
            assert metadata is not None
            identity = {name: metadata.findtext("{*}" + name) for name in ["id", "version"]}
            repository = metadata.find("{*}repository")
            assert identity == {"id": item["package_id"], "version": "0.10.2"}
            assert repository is not None and repository.get("commit") == publication["sourceGitSha"]
            assert ".signature.p7s" in archive.namelist()
        packages.append({"package_id": item["package_id"], "path": str(path), "sha256": digest,
                         "byte_length": len(content), "nuspec": identity,
                         "source_git_sha": repository.get("commit"), "feed_identity": record["feedIdentity"],
                         "signature_entry_present": True, "current_signature_verification": "NOT_RUN"})
    assert len(anchors) == 15 and len(packages) == 7
    output = {"task": "WP02-27", "captured_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "main_baseline": MAIN_SHA, "platform_baseline": PLATFORM_SHA,
              "admission_git_bytes_sha256": sha(raw), "original_design_sha256": BODY_SHA,
              "original_design_path": str(body), "source_anchors": anchors,
              "formal_publication_path": publication_path, "formal_publication_sha256": sha(publication_bytes),
              "formal_source_git_sha": publication["sourceGitSha"], "cached_packages": packages,
              "scope": "Read-only original source, raw bytes and archive metadata; no signature or current revocation success claim"}
    target = ROOT / "docs/development/evidence/WP02-27/inputs.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print("Verified 15 committed source anchors, original design SHA256 and 7/7 immutable cache/archive identities; signature evaluation NOT_RUN.")


if __name__ == "__main__":
    main()
