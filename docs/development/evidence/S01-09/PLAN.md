# S01-09 implementation boundary

Baseline Platform `30bd23af6808d217a23878bd9437513043c52834`; Main input and source pins at `dfc043b1473dcea51d34ff87a7808692349c278a`. Assignment/entry confirmed at Main `4ab979e2f391c5a5645da0d1586781e824a9f8cd`.

1. Import both fixed S01 input blobs and three real native identity assets with their exact hashes; preserve original byte encoding in Git and test output. No dependency on an archive directory when running tests.
2. Add named native routing APIs, using the pinned S01 manifest as trusted configuration. Validate the complete OpenAPI identity or registry filename/codec/hash and actual asset bytes. Select current/frozen out of band, with no fallback and no new Owner envelope fields. Existing CloudEvent bundle remains single-version per event type.
3. Add an owned original JSON byte snapshot with SHA256, defensive copies and byte comparison. Strict duplicate rejection applies before interpretation. Required-null projection and structural equality adopt the pinned design semantics; structural equality never provides replay bytes or a digest.
4. Add optional explicit manifest/version pinning to the existing bundle loader, retaining its strict triple and schema/example hash checks. Apply duplicate rejection to bundle manifests and CloudEvent business data; preserve P08's explicitly accepted root traceparent/tracestate duplicate-discard contract. Native JSON has no such exception.
5. Execute all 35 declared vectors and boundary/mutation/legacy regression tests locally. Retain the test-first failures, compiled source identity, final output and limitations; perform one complete diff review.
6. Publish the candidate and DELIVERY to COORD after current workflow-filter checks. COORD owns serial integration and Main state files. No package release, database, transport transaction, production replay or deployment is included.

Design source for structural numeric equality and null-only behavior: Main `docs/design-handbook/remediation/assets/core-types/DesignRevisionTypes.cs`, Git blob `9725563155f695d923532ee7c136f88e15942469`, SHA256 `a7ab417cabf201e8ec7b716360539b18fc0db751d180bb05b0b18f7dc3a3c3db`. Duplicate traversal follows the existing Platform `Cp6SloEvidenceDocument` approach. Neither imports the pending S01-02 implementation.
