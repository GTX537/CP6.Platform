"""Read fixed Git blobs; preserve original AC and record separate local subassertions.

Run with Python 3.10+: capture_inputs.py --main-repository <Main checkout>
Writes only this task evidence directory. Does not update Main or its acceptance states.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

MAIN = "e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47"
PLATFORM = "dc4c0ab216974339ab3c2cebaaf4bb92c9e9b351"
ASSIGNMENT = "e20740e37e20bc0210e0b32c5670c2bce962002a"
OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[3]

# Exact original obligation IDs; references name local test methods, never production acceptance.
MAPPING = {
    "CTX01-A1": (["ValidBinding_ExposesExactContextAndLeastScopes_ThenRevokes", "TrustedResolver_EstablishesReadOnlyContext_ThenClearsIt"], "Main/CRM real verifier and precise runtime snapshot evidence"),
    "CTX01-A2": (["UntrustedOrMismatchedBinding_RejectsBeforeHandler", "LegacyNullOrInvalidSnapshot_DoesNotInvokeHandlerOrLeak", "MissingContext_FailsClosed_AndDoesNotInvokeApplication"], "Main/CRM actual business ingress rejection and PostgreSQL connection/write counts"),
    "CTX01-A3": (["SerialAndNested_ABA_RestoreOuterAndClearTopLevel", "OverlappingAsyncOperations_OnSameAccessor_KeepSeparateTenants", "CapturedChildExecutionContext_IsRevokedAfterParentScopeEnds", "ExceptionsDuringEachPhase_ClearScopeAndRestoreOuter", "CancellationInEachPhase_PropagatesAndClears", "PendingAuthorityCancellation_ClearsMaskedNestedScopeAndRestoresOuter"], "Each real host's cancellation, exception and reuse paths"),
    "CTX01-A4": (["OptInRegistration_SharesAccessor_AndIgnoresCallerTenantIssuerActor", "UntrustedOrMismatchedBinding_RejectsBeforeHandler"], "Main/CRM trusted production adapters, authenticated actor to tenant mapping and UI requests"),
    "CTX01-A5": (["EachRetry_ReverifiesCurrentServiceAndRegistration_NotHistoricalRequester", "NonHttpEntry_RejectsHistoricalHumanOrPublicAsExecutor", "EntryWrapper_RejectsCapturedHttpSource", "MessageTraceAlone_CannotEstablishIdentity"], "Real worker/job/producer credentials, queue retry adoption and current application registration"),
    "CTX01-A6": (["ExplicitHealthBranch_BypassesBusinessContextWithoutInventingTenant"], "Main/CRM actual health/business route split and zero business database connections"),
    "CTX01-A7": (["ResolutionFailures_FailClosedWithExplicitStatusAndNoSensitiveBody", "HandlerTypedError_IsNotReclassifiedAsAuthenticationFailure", "MissingContext_FailsClosed_AndDoesNotInvokeApplication"], "Actual JWT challenge and old host compatibility; UI organization switch/cache old-data regression"),
    "CTX02-A1": (["InvalidCurrentRegistrationAndSource_NeverReachBusiness"], "IAM organization GUID lifecycle and tenant-directory persistence"),
    "CTX02-A2": (["UntrustedOrMismatchedBinding_RejectsBeforeHandler", "EvenMatchingRegistration_CannotLegalizeInvalidIssuer", "IssuerComparison_IsExactAndDoesNotNormalizeTrailingSlashOrCase"], "Authoritative registered region/issuer catalog and PostgreSQL routing/connection rejection"),
    "CTX02-A3": (["ValidBinding_ExposesExactContextAndLeastScopes_ThenRevokes", "EachRetry_ReverifiesCurrentServiceAndRegistration_NotHistoricalRequester"], "Owner DB resolver must use and trace the same registration version; not implemented by Context"),
    "CTX02-A4": ([], "IAM suspension/history retention and Owner separate history-read/new-effect policies; do not globally disable legitimate reads"),
    "CTX02-A5": ([], "IAM slug/region lifecycle and explicit data-migration process; no directory or migration implementation here"),
    "CTX02-A6": (["Interface_IsExactlyTheApprovedReadOnlyContract", "Constructor_PreservesTrustedValuesWithoutGeneratingDefaults", "ResolutionMasksOuterContext_AndLegacyDoesNotInheritTrustedEvidence", "HumanNullableIdentityAndOriginalStrings_ArePreservedWithoutNewLegacyLimits"], "Old consumer serialized snapshot/CloudEvent/runtime adoption verification; evidence absence conveys no new guarantee"),
    "CTX02-A7": ([], "Owner final transaction gate, mapping epoch/version fencing and actual single-database writes"),
    "CTX03-A1": (["ValidBinding_ExposesExactContextAndLeastScopes_ThenRevokes", "InvalidCurrentRegistrationAndSource_NeverReachBusiness", "Collections_AreFrozenBeforeAuthorityAwait_AndEvidenceCannotBeMutated"], "P03 signature/profile and IAM actual service-token/application-registration integration"),
    "CTX03-A2": (["InvalidCurrentRegistrationAndSource_NeverReachBusiness", "NonHttpEntry_RejectsHistoricalHumanOrPublicAsExecutor"], "Existing JWT required-claim enforcement and actual human/service distinct audience/profile configuration"),
    "CTX03-A3": (["InvalidCurrentRegistrationAndSource_NeverReachBusiness", "AuthorityUnavailable_IsSafeTypedFailure_WithNoFallback", "ExpiryIsCheckedAfterAuthorityAwait_NotBeforeIt"], "Existing P03/JWKS key availability and real host configuration failure behavior"),
    "CTX03-A4": (["EachRetry_ReverifiesCurrentServiceAndRegistration_NotHistoricalRequester", "UntrustedOrMismatchedBinding_RejectsBeforeHandler"], "Real revocation provider and Owner final business gate for effects after context resolution"),
    "CTX03-A5": ([], "P03 existing fixed issuer/profile key rotation and bounded cache; no auth/cache changes"),
    "CTX03-A6": (["ResolutionFailures_FailClosedWithExplicitStatusAndNoSensitiveBody", "AuthorityUnavailable_IsSafeTypedFailure_WithNoFallback"], "All real host logs/traces/responses and PII audit; adapters must return opaque references, no credentials"),
    "CTX03-A7": ([], "CRM browser and Main service actual profile/runtime preservation; library does not register or replace authentication"),
    "CTX04-A1": ([], "Each actual HTTP/worker/consumer owner must create precise adoption record; see WP02-01 doc inventory"),
    "CTX04-A2": ([], "Package/reference/registration/enabled/runtime/business evidence separately required; fixture is not production"),
    "CTX04-A3": (["NonHttpEntry_RejectsHistoricalHumanOrPublicAsExecutor", "EachRetry_ReverifiesCurrentServiceAndRegistration_NotHistoricalRequester"], "Real CRM browser/sidecar and Main service identity/profile boundaries"),
    "CTX04-A4": ([], "Consumer S6 content gate ordering before Duplicate/IgnoredOutOfOrder; no consumer business change here"),
    "CTX04-A5": ([], "Owner missing-final-gate fail-closed new effects with legal historical reads retained"),
    "CTX04-A6": ([], "Actual old/new writer inventory and transition proof; no old consumer stopped or package auto-published"),
    "CTX04-A7": ([], "Owner typed replay three-action routing; no context permission to reorder")
}


def blob(repo, commit, path):
    return subprocess.check_output(["git", "-C", str(repo), "-c", "core.longpaths=true", "show", f"{commit}:{path}"])


def write_json(name, value):
    (OUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--main-repository", type=Path, required=True)
    args = parser.parse_args()
    sources = [
        (args.main_repository, MAIN, "docs/development/assessments/20261011/catalogs/foundation.json", "6af601f92d8a3f3b347358313bd6d3c199581a0fd93650b3c6c7090249329417"),
        (args.main_repository, MAIN, "docs/design-handbook/modules/PLT-CTX-01.md", "b7587dd2f62a2810212b8fdc7558cbac65262bd7e8a96418f6e5c0885cda97d4"),
        (args.main_repository, MAIN, "docs/design-handbook/architecture/02-postgresql-tenancy.md", "dfcaea44365d92f7457db783908dd8eb6f1eaa7651c67016bdff68281a0148c3"),
        (args.main_repository, MAIN, "docs/development/acceptance/postgresql-tenancy.json", "b56363bd547034d45e5556bf112b41f71cbb4d7b9dc565de25876d297deefbd5"),
        (ROOT, PLATFORM, "src/CP6.Platform.AspNetCore/RequestContextMiddleware.cs", "0a586bbf0fe0b983514b4e45e8124729f38aa065ebee3b92a8c446e2dc95e211"),
        (args.main_repository, ASSIGNMENT, "docs/development/evidence/wave4-dispatch-20261013/WP02-01-admission.md", "dcae64d48bb43ab92bb2be81d37a33f38ee37edbc7ae293b298c08e5d78721ba"),
        (args.main_repository, ASSIGNMENT, "docs/development/evidence/wave4-dispatch-20261013/WP02-01-admission.json", "e9c80a5c5585152d8851c40948311614578439c32dc6b284fa933e7081bf490c")
    ]
    dispatch = json.loads(blob(args.main_repository, ASSIGNMENT, "docs/development/dispatch/20261013-wave4/dispatch.json"))
    sources.extend((args.main_repository, p["commit"], p["path"], p["sha256"]) for p in dispatch["input_pins"])
    records = []
    for repo, commit, path, expected in sources:
        digest = hashlib.sha256(blob(repo, commit, path)).hexdigest()
        assert digest == expected, (path, digest, expected)
        records.append(dict(repository="Platform" if repo == ROOT else "Main", commit=commit,
                            path=path, representation="GIT_BLOB_BYTES", sha256=digest, verified=True))
    catalog = json.loads(blob(args.main_repository, MAIN, sources[0][2]))["targets"][13]
    assert catalog["id"] == "PLT-CTX-01" and len(catalog["cases"]) == 28
    assert set(MAPPING) == {c["source_case_id"] for c in catalog["cases"]}
    original = catalog["source_sets"][0]
    original_bytes = (args.main_repository / original["archive_path"]).read_bytes()
    assert hashlib.sha256(original_bytes).hexdigest() == original["sha256"]
    records.append(dict(repository="Main archive", path=original["archive_path"], representation="ARCHIVE_RAW_BYTES",
                        sha256=original["sha256"], verified=True))
    results = []
    for name in ("full-unit.trx", "full-http.trx"):
        path = OUT / "test-results" / name
        if path.exists():
            results.extend((r.attrib["testName"], r.attrib["outcome"], name)
                           for r in ET.parse(path).iter() if r.tag.endswith("}UnitTestResult"))
    mapped = []
    rows = []
    for case in catalog["cases"]:
        refs, remainder = MAPPING[case["source_case_id"]]
        local = []
        for ref in refs:
            matches = [(n, o, f) for n, o, f in results if ref in n]
            status = "PASS_LIBRARY_ONLY" if matches and all(o == "Passed" for _, o, _ in matches) else "NOT_RUN"
            local.append(dict(test=ref, status=status, matched_rows=len(matches), evidence=sorted({f for _, _, f in matches})))
        mapped.append(dict(original=case, library_subassertions=local, remaining_owner_acceptance=remainder,
                           original_acceptance_state="NOT_RUN"))
        label = "<br>".join(f"`{x['test']}` ({x['status']})" for x in local) or "无本次运行子断言"
        rows.append(f"| {case['source_case_id']} | {case['assertion']} | {label} | {remainder} |")
    pg = next(c for c in json.loads(blob(args.main_repository, MAIN, sources[3][2]))["cases"] if c["id"] == "PGT-004")
    assert pg["status"] == "NOT_RUN"
    write_json("PROVENANCE.json", dict(task="WP02-01", main_source=MAIN, assignment=ASSIGNMENT, platform_baseline=PLATFORM,
        input_package="CP6-S01-CONTRACT-INPUTS/1.0.0", directly_assigned_fixed_vectors=0, sources=records,
        start_handoff=dict(commit="bbfebdd46d97fee3ac6cf10dbbf6e937220e0c0b", raw_sha256="c1d474e7132ae9da34d75a491d5607ea8dda643d3887089f8122e70de6ad4ed9", remote_raw_verified=True)))
    write_json("acceptance-map.json", dict(task="WP02-01", original_case_count=28, original_cases=mapped,
        postgresql_scenario=pg, postgresql_execution="NOT_RUN", target_done_claim=False))
    text = """# WP02-01 验收责任映射

原 28 条 AC 来自固定 Main catalog `/targets/13`；逐项原文、来源行/hash/state 完整保存在 [acceptance-map.json](acceptance-map.json)。本表的 PASS_LIBRARY_ONLY 只指本任务实际 TRX 中匹配且通过的子断言，不改原 AC 的 NOT_RUN，不证明 PLT-CTX-01 整体 DONE。没有将 fixture 认证、包引用或库 scope 测试改称实际宿主/业务验收。

| 原 AC | 原断言 | 本次库子断言（精确测试方法） | 剩余 Owner/宿主验收，NOT_RUN |
|---|---|---|---|
""" + "\n".join(rows) + """

## PGT-004：缺失或伪造租户输入

原条目原样保存在 acceptance-map.json，status=NOT_RUN。原范围为 CTX01-A2/A4/A6 与 CTX02-A2：无 tenant claim/非法 issuer、A 自称 B、未知 region、body/header schema，以及 tenantless 健康探针。当前库测试证明缺/假/跨租户事实不能进入 fixture 业务回调，HTTP 不读取自报身份，显式健康分支不调用 verifier/authority，失败响应正文为空。

仍需 Main/CRM 实际 resolver、可信目录和 PostgreSQL 数据库接入，记录**取得业务连接前**拒绝、连接获取次数为零、健康探针无业务库调用、无 A1/public fallback、无业务写集、错误不泄露目录。本任务没有数据库，因此不能声称已证明这些连接/写集事实。

## 保留范围

0 条直接分配的 S01 固定向量；整个 247 运行场景不因局部测试自动 PASS。41 条历史静态 REFERENCE_ONLY、缺失 ITER/REL 原件与 WP05 PROPOSED 均保留。JWT 密码学/轮换、CRM/Main profile、S6 内容门、最终授权事务、单 writer、typed replay、数据迁移、真实服务登记与宿主运行记录均由各自责任任务验收。

重建：在 Platform 根目录运行 `python docs/development/evidence/WP02-01/capture_inputs.py --main-repository <Main checkout>`。输入固定到 PROVENANCE 中的 Git SHA；只写本任务目录，不改变 Main 台账/验收状态。
"""
    (OUT / "ACCEPTANCE.md").write_text(text, encoding="utf-8")
    print(f"Verified {len(records)} sources; preserved 28 original AC + PGT-004; read {len(results)} local TRX rows.")


if __name__ == "__main__":
    main()
