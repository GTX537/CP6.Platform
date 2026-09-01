# P09 non-production runtime rehearsal

| Field | Value |
| --- | --- |
| Status | `Published / Consumer Candidate` |
| Repository version | `0.9.0.0` |
| Deployment package candidate | `CP6.Platform.Deployment 0.9.0-alpha.1` |
| Completed stages | `P09-S01`, `P09-S02`, `P09-S03`, `P09-S04` |
| Publication stage | `P09-S04: Complete`; independently verified evidence is in `P09-PUBLICATION.md` |
| Deferred stages | `P09-S05`, `P09-S06` |

This status describes the published producer package and its exact-main rehearsal evidence. `CP6.Platform.Deployment 0.9.0-alpha.1` was published from Platform commit `1c40f21e38929abaaa6006f69ee70d4492890661` by run `33480300468` and independently matched at SHA-256 `e820d1771ed004b4a7089d008eef3bb2aca4fe35e4912d67057840373c4952cb`. CRM consumption, public project-memory synchronization, real environment rollout, and the final reusable-state decision remain separate later stages.

## Scope

P09-S01 through P09-S03 provide:

- a strict Draft 2020-12 non-production Profile and rehearsal Evidence contract;
- the dependency-free `CP6.Platform.Deployment` validation package candidate;
- a real Docker Compose rehearsal using Dapr `1.18.2`, Kafka `4.3.1`, generated one-run credentials, exact Topic/ACL rules, deterministic `nameformat` resolution through Docker DNS, positive invocation/PubSub checks, and four negative boundaries;
- Kubernetes base and CI overlay assets with deterministic Kustomize render, cross-object policy validation, client dry-run, and no Kubernetes Secret object;
- canonical, content-addressed execution evidence plus exact-project teardown and zero-residue checks.

The probe reuses the existing P04 synthetic event type. It does not add a business event, CRM route, worker process, database, cloud resource, Registry action, real cluster action, or environment deployment.

## Prerequisites

- Git with the repository checked out at the commit being verified.
- PowerShell 7 (`pwsh`).
- .NET 8 SDK. If multiple SDK majors are installed, set `DOTNET_HOST_PATH` to the .NET 8 host.
- Docker Engine API `1.49` or newer (Docker Engine `28.1+`) with Docker Compose `2.36.0` or newer for the offline Kubernetes gate and real rehearsal.
- Enough local resources to start one Kafka broker, three Dapr sidecars, and bounded one-off helper containers.

No host `kubectl`, cloud account, kubeconfig, external Kafka, Registry credential, or committed secret value is required.

The three Dapr sidecars keep the isolated dual-network topology, fixed interfaces, and runtime gateway priority. Service invocation does not depend on self-hosted mDNS: the Dapr `nameformat` resolver maps each AppId to its exact runtime-network Docker DNS alias on the pinned internal gRPC port `50002`, whose cross-container listener is fixed to `0.0.0.0` rather than left to host-specific empty-address handling. The rehearsal configuration also fixes Dapr trace sampling to `1`, because the acceptance matrix validates every invocation and Pub/Sub trace rather than accepting Dapr's probabilistic default. The Pub/Sub assertion accepts Dapr's sampled consumer span between the CloudEvent publisher span and the receiver HTTP span, while still requiring the same Trace ID and distinct nonzero publisher, Dapr-parent, and receiver Span IDs. Failure diagnosis exposes only closed DNS/TCP categories, never raw container logs, addresses, paths, or credentials. These settings add no discovery service, trace exporter, static IP, host network, or application access to the Kafka runtime network, and remain deterministic on hosted Azure runners where mDNS may be unavailable.

Runtime files are populated through bounded one-off containers as their exact target UID/GID with mode `0600`. The three Dapr component mount directories are then sealed as target-owned `0700`, because Dapr watches `/components` and must be able to enumerate that directory. The helper runs as root only for an exact temporary component-directory bind and performs only `chown <target-uid:gid> /input` plus `chmod 0700 /input`; it receives no Docker socket or credential value. Kafka client and Dapr secret directories do not need directory watches and remain host-owned `0711`, preserving traversal without group/other read access. Every resulting directory/file group is probed from its target runtime identity before any long-lived service starts. On Unix, after Compose has stopped and removed every runtime consumer, an exact-directory, `--network none` helper returns only those three directory owners to the bounded host UID/GID so the runner can delete its temporary tree; the helper carries the exact project/config labels so any abnormal residue remains discoverable by the same cleanup guard.

## Exact local commands

Run the static, script, package, and offline Kubernetes contract:

```powershell
pwsh ./eng/verify.ps1 -P09Contract
pwsh ./eng/pack-p09.ps1 -VerifyReproducible
```

Run the full real rehearsal against the current exact commit:

```powershell
$expectedGitSha = (git rev-parse HEAD).Trim()
pwsh ./eng/verify.ps1 -P09Real -ExpectedGitSha $expectedGitSha
```

The real runner rejects a malformed SHA, a SHA different from `HEAD`, or dirty canonical P09 assets. It never converts an unavailable runtime into a passing result.

## Outcomes

| Outcome | Meaning | Exit code |
| --- | --- | ---: |
| `Passed` | Every selected contract or runtime check passed and the real rehearsal proved zero residue | `0` |
| `NotRun` | Local Docker/Compose prerequisites were unavailable, so no passing Evidence was produced | `2` |
| `Failed` | A contract, process, runtime, evidence, secret scan, or cleanup condition failed closed | `1` |

In CI, the dedicated `ubuntu-p09-non-production-runtime` job treats unavailable Docker as `Failed`; it cannot use the local `NotRun` allowance.

## Artifacts and evidence

- Verification summaries and JUnit: `artifacts/verify/p09contract/` and `artifacts/verify/p09real/`.
- Real rehearsal output: `artifacts/p09-rehearsal/<run-id>/`.
- Kubernetes output: `artifacts/p09-kubernetes/<run-id>/`.
- Successful canonical evidence: `rehearsal-evidence.v1.json`.
- Kubernetes contract result: `kubernetes-contract-result.v1.json`.

The successful rehearsal evidence binds the Platform Git SHA, repository and package versions, Profile/Compose/Kubernetes hashes, fixed image names and resolved digests, Topic and ACL tuples, stable check IDs, trace topology, timestamps, and teardown result. `temporaryDirectoryRemoved` must be `true`; container, network, volume, and locally built image counts must all be zero. The validator computes the SHA-256 over strict compact UTF-8 canonical JSON with no trailing whitespace.

Evidence and retained logs may contain safe identifiers and stable failure categories only. They must not contain credential values, reversible credential encodings, connection strings, host user paths, free-text container errors, environment dumps, or the generated runtime directory.

## Cleanup semantics

The runner owns one unique `cp6-p09-<identity>` Compose project. Its bounded cleanup is equivalent to:

```text
docker compose down --volumes --remove-orphans --rmi local
```

It then queries exact P09 labels for remaining containers, networks, volumes, and locally built fixture images, closes all credential users, and deletes only its validated temporary tree. A cleanup error overrides an earlier runtime success. The runner never calls a broad Docker prune and never deletes an unverified path.

On Unix, target-owned `0700` Dapr component directories are returned to the current bounded host UID/GID only after the first all-profile Compose down succeeds. The fixed Kafka helper has network disabled, receives no Secret, mounts one validated component directory at a time, and exits before label-based residue queries. Failure to return ownership is the stable `runtime-release` cleanup failure and prevents passing evidence.

## Kubernetes offline boundary

The Kubernetes gate renders the fixed base plus CI overlay twice and requires identical SHA-256 values. Source mounts are read-only and container network access is disabled. Because `kubectl apply --dry-run=client` still performs a read of current object state, the gate supplies a loopback-only TLS 404 sentinel inside the isolated helper container; the sentinel rejects POST, PUT, PATCH, and DELETE and records any attempted mutation. This proves client-side behavior without contacting or mutating a cluster.

The CI overlay uses nondeployable `example.invalid` image identities and `cp6.io/nondeployable=true`. Its passing result is a render/policy contract, not environment acceptance.

## Troubleshooting

1. Read `artifacts/verify/p09real/summary.json` and the named bounded log to identify the first failed check.
2. Inspect `run-log.v1.jsonl` for stable stages such as `kubernetes-policy`, `provision`, `runtime-matrix`, `image-digest`, and `zero-residue`.
3. If the result is `NotRun`, confirm Docker Engine is reachable, `docker version --format '{{.Server.APIVersion}}'` is at least `1.49`, and `docker compose version --short` is at least `2.36.0`; do not bypass either version check.
4. If service invocation fails, use the closed `DiagnosticCategory` to distinguish publisher DNS/API from target DNS/internal-gRPC reachability; then confirm each runtime alias equals its Dapr AppId, each sidecar loads `name-resolution.yaml`, trace sampling remains `1`, and internal gRPC remains `0.0.0.0:50002`. Do not fall back to mDNS, raw-log publication, or probabilistic trace acceptance in hosted Azure CI.
5. If a Dapr sidecar exits during startup, inspect only the bounded, redacted `sidecar-exit-diagnostic.v1.json`. A watcher `permission denied` means a Dapr component directory lost its target-owned `0700` seal; do not make the whole temporary tree readable or change unrelated Kafka/secret directories from `0711`.
6. If an exact-SHA check fails, commit or deliberately discard only the task's own P09 changes, then rerun with the new `HEAD`; do not weaken the check.
7. If cleanup fails, inspect only resources carrying the exact run's P09 project labels. Do not use global prune commands.
8. Reproduce failures with the same command and preserve the bounded artifact directory. Never add credentials or raw environment data to diagnostics.

## Stage ledger

| Stage | Candidate state | Exit boundary |
| --- | --- | --- |
| `P09-S01` | Complete on Platform main | Profile, Schema, validator, independent package boundary, positive/negative tests |
| `P09-S02` | Complete on Platform main | Exact-SHA real Dapr/Kafka matrix, canonical evidence, zero residue |
| `P09-S03` | Complete on Platform main | Deterministic offline Kubernetes render/dry-run/policy matrix |
| `P09-S04` | Complete; published and independently verified | Exact-main immutable package publication, Registry download match, and retained hashes |
| `P09-S05` | Not started | CRM fixed-version black-box consumption and locator evidence |
| `P09-S06` | Not started | Public project-memory synchronization and final Platform audit |

The allowed state at this boundary is only `Published / Consumer Candidate`: S01-S04 complete; S05-S06 pending.
