"""Read-only complete authenticated version observations; never reserves a slot."""
import datetime
import hashlib
import json
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[3]
out = root / "docs/development/evidence/WP02-02/formal-registration/feed-version-observation.json"
actor = subprocess.run(["gh", "api", "user", "--jq", ".login"], capture_output=True, text=True)
if actor.returncode:
    raise SystemExit("Authenticated principal UNKNOWN; observation stopped")
result = {"mode": "READ_ONLY_NOT_RESERVATION", "actor_login": actor.stdout.strip(), "packages": []}
for suffix in ("Abstractions", "AspNetCore", "Contracts", "Deployment", "EntityFramework", "Messaging", "Release"):
    package = "CP6.Platform." + suffix
    command = ["gh", "api", f"users/GTX537/packages/nuget/{package}/versions?per_page=100", "--paginate", "--slurp"]
    run = subprocess.run(command, capture_output=True)
    row = {"packageId": package, "command": command, "observedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "exitCode": run.returncode, "raw_sha256": hashlib.sha256(run.stdout).hexdigest(), "stderr": run.stderr.decode("utf-8", errors="replace")}
    row["proposed0103"] = "UNKNOWN"
    if run.returncode == 0:
        pages = json.loads(run.stdout)
        versions = [v["name"] for page in pages for v in page]
        row.update(pageCount=len(pages), versions=versions, proposed0103="OCCUPIED" if "0.10.3" in versions else "NOT_LISTED_AT_OBSERVATION")
    result["packages"].append(row)
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"actor": result["actor_login"], "slots": {r["packageId"]: r["proposed0103"] for r in result["packages"]}}))
raise SystemExit(0 if all(r["proposed0103"] == "NOT_LISTED_AT_OBSERVATION" for r in result["packages"]) else 1)
