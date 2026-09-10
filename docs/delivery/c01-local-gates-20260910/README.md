# C01 final records under local validation

The owner explicitly authorized replacing necessary ordinary Actions checks with local verification on 2026-09-10 because the monthly allowance was exhausted. This completes the remaining Platform record delivery for the same C01 work; published packages are unchanged.

Only these five main status requirements were removed: ubuntu-latest, windows-latest, ubuntu-dapr-kafka, ubuntu-sql-server and ubuntu-p09-non-production-runtime. The before/after snapshots were compared after excluding required_status_checks; every other protection field is identical. PR reviews, administrator enforcement, force-push/deletion restrictions and formal package publication requirements retain their existing settings. No successful Actions status was fabricated.

The ordinary platform-validation workflow was paused before creating the PR, and its event block now contains workflow_dispatch only. All jobs, commands and permissions are byte-for-byte unchanged. After normal integration and remote trigger verification, its manual entry is restored. The separate formal-package, signing, RFC3161 and publication workflows are unchanged; the RFC3161 PR path filter does not match this delivery.

Validation reuses the complete actual 72-case C01 acceptance and published package identities linked from CHANGELOG.md. Documentation requires no package build or repeated business tests. The event-block diff, unchanged workflow body, whitespace checks and remote source containment are verified locally/read-only; no new Actions run or package publication is authorized.