# WP02-01 验收责任映射

原 28 条 AC 来自固定 Main catalog `/targets/13`；逐项原文、来源行/hash/state 完整保存在 [acceptance-map.json](acceptance-map.json)。本表的 PASS_LIBRARY_ONLY 只指本任务实际 TRX 中匹配且通过的子断言，不改原 AC 的 NOT_RUN，不证明 PLT-CTX-01 整体 DONE。没有将 fixture 认证、包引用或库 scope 测试改称实际宿主/业务验收。

| 原 AC | 原断言 | 本次库子断言（精确测试方法） | 剩余 Owner/宿主验收，NOT_RUN |
|---|---|---|---|
| CTX01-A1 | 可信resolver成功时应用观察精确快照且结束Current为空 | `ValidBinding_ExposesExactContextAndLeastScopes_ThenRevokes` (PASS_LIBRARY_ONLY)<br>`TrustedResolver_EstablishesReadOnlyContext_ThenClearsIt` (PASS_LIBRARY_ONLY) | Main/CRM real verifier and precise runtime snapshot evidence |
| CTX01-A2 | null/Empty tenant拒绝且业务未进入 | `UntrustedOrMismatchedBinding_RejectsBeforeHandler` (PASS_LIBRARY_ONLY)<br>`LegacyNullOrInvalidSnapshot_DoesNotInvokeHandlerOrLeak` (PASS_LIBRARY_ONLY)<br>`MissingContext_FailsClosed_AndDoesNotInvokeApplication` (PASS_LIBRARY_ONLY) | Main/CRM actual business ingress rejection and PostgreSQL connection/write counts |
| CTX01-A3 | 异常和取消清理，不串下个请求 | `SerialAndNested_ABA_RestoreOuterAndClearTopLevel` (PASS_LIBRARY_ONLY)<br>`OverlappingAsyncOperations_OnSameAccessor_KeepSeparateTenants` (PASS_LIBRARY_ONLY)<br>`CapturedChildExecutionContext_IsRevokedAfterParentScopeEnds` (PASS_LIBRARY_ONLY)<br>`ExceptionsDuringEachPhase_ClearScopeAndRestoreOuter` (PASS_LIBRARY_ONLY)<br>`CancellationInEachPhase_PropagatesAndClears` (PASS_LIBRARY_ONLY)<br>`PendingAuthorityCancellation_ClearsMaskedNestedScopeAndRestoresOuter` (PASS_LIBRARY_ONLY) | Each real host's cancellation, exception and reuse paths |
| CTX01-A4 | 浏览器tenant/actor字段不能覆盖可信绑定 | `OptInRegistration_SharesAccessor_AndIgnoresCallerTenantIssuerActor` (PASS_LIBRARY_ONLY)<br>`UntrustedOrMismatchedBinding_RejectsBeforeHandler` (PASS_LIBRARY_ONLY) | Main/CRM trusted production adapters, authenticated actor to tenant mapping and UI requests |
| CTX01-A5 | worker独立scope与当前service身份，历史requester不冒执行身份 | `EachRetry_ReverifiesCurrentServiceAndRegistration_NotHistoricalRequester` (PASS_LIBRARY_ONLY)<br>`NonHttpEntry_RejectsHistoricalHumanOrPublicAsExecutor` (PASS_LIBRARY_ONLY)<br>`EntryWrapper_RejectsCapturedHttpSource` (PASS_LIBRARY_ONLY)<br>`MessageTraceAlone_CannotEstablishIdentity` (PASS_LIBRARY_ONLY) | Real worker/job/producer credentials, queue retry adoption and current application registration |
| CTX01-A6 | tenantless健康路径不构造假租户也不访问业务 | `ExplicitHealthBranch_BypassesBusinessContextWithoutInventingTenant` (PASS_LIBRARY_ONLY) | Main/CRM actual health/business route split and zero business database connections |
| CTX01-A7 | 401/403/503分类和旧Host兼容准确，UI切组织后不回显旧资料 | `ResolutionFailures_FailClosedWithExplicitStatusAndNoSensitiveBody` (PASS_LIBRARY_ONLY)<br>`HandlerTypedError_IsNotReclassifiedAsAuthenticationFailure` (PASS_LIBRARY_ONLY)<br>`MissingContext_FailsClosed_AndDoesNotInvokeApplication` (PASS_LIBRARY_ONLY) | Actual JWT challenge and old host compatibility; UI organization switch/cache old-data regression |
| CTX02-A1 | tenant GUID稳定且organizationId等值 | `InvalidCurrentRegistrationAndSource_NeverReachBusiness` (PASS_LIBRARY_ONLY) | IAM organization GUID lifecycle and tenant-directory persistence |
| CTX02-A2 | 未注册region或issuer不路由其他库 | `UntrustedOrMismatchedBinding_RejectsBeforeHandler` (PASS_LIBRARY_ONLY)<br>`EvenMatchingRegistration_CannotLegalizeInvalidIssuer` (PASS_LIBRARY_ONLY)<br>`IssuerComparison_IsExactAndDoesNotNormalizeTrailingSlashOrCase` (PASS_LIBRARY_ONLY) | Authoritative registered region/issuer catalog and PostgreSQL routing/connection rejection |
| CTX02-A3 | snapshot与数据库选择同登记版本可追溯 | `ValidBinding_ExposesExactContextAndLeastScopes_ThenRevokes` (PASS_LIBRARY_ONLY)<br>`EachRetry_ReverifiesCurrentServiceAndRegistration_NotHistoricalRequester` (PASS_LIBRARY_ONLY) | Owner DB resolver must use and trace the same registration version; not implemented by Context |
| CTX02-A4 | 暂停不改历史tenant，read与新作用分门 | 无本次运行子断言 | IAM suspension/history retention and Owner separate history-read/new-effect policies; do not globally disable legitimate reads |
| CTX02-A5 | 改slug不重建身份，改region不自动迁数据 | 无本次运行子断言 | IAM slug/region lifecycle and explicit data-migration process; no directory or migration implementation here |
| CTX02-A6 | context snapshot保持原格式，旧消费者兼容缺evidence时不冒新版保证 | `Interface_IsExactlyTheApprovedReadOnlyContract` (PASS_LIBRARY_ONLY)<br>`Constructor_PreservesTrustedValuesWithoutGeneratingDefaults` (PASS_LIBRARY_ONLY)<br>`ResolutionMasksOuterContext_AndLegacyDoesNotInheritTrustedEvidence` (PASS_LIBRARY_ONLY)<br>`HumanNullableIdentityAndOriginalStrings_ArePreservedWithoutNewLegacyLimits` (PASS_LIBRARY_ONLY) | Old consumer serialized snapshot/CloudEvent/runtime adoption verification; evidence absence conveys no new guarantee |
| CTX02-A7 | 映射变化在提交门被识别，无同对象双库写 | 无本次运行子断言 | Owner final transaction gate, mapping epoch/version fencing and actual single-database writes |
| CTX03-A1 | 合法配置下有效service token经应用登记才获得相应范围 | `ValidBinding_ExposesExactContextAndLeastScopes_ThenRevokes` (PASS_LIBRARY_ONLY)<br>`InvalidCurrentRegistrationAndSource_NeverReachBusiness` (PASS_LIBRARY_ONLY)<br>`Collections_AreFrozenBeforeAuthorityAwait_AndEvidenceCannotBeMutated` (PASS_LIBRARY_ONLY) | P03 signature/profile and IAM actual service-token/application-registration integration |
| CTX03-A2 | 人类/服务audience分离且未知必需claim拒绝 | `InvalidCurrentRegistrationAndSource_NeverReachBusiness` (PASS_LIBRARY_ONLY)<br>`NonHttpEntry_RejectsHistoricalHumanOrPublicAsExecutor` (PASS_LIBRARY_ONLY) | Existing JWT required-claim enforcement and actual human/service distinct audience/profile configuration |
| CTX03-A3 | 过期/无有效key/不可用配置不扩大权限 | `InvalidCurrentRegistrationAndSource_NeverReachBusiness` (PASS_LIBRARY_ONLY)<br>`AuthorityUnavailable_IsSafeTypedFailure_WithNoFallback` (PASS_LIBRARY_ONLY)<br>`ExpiryIsCheckedAfterAuthorityAwait_NotBeforeIt` (PASS_LIBRARY_ONLY) | Existing P03/JWKS key availability and real host configuration failure behavior |
| CTX03-A4 | token signature有效但已撤销/tenant停用仍按当前应用门拒绝新作用 | `EachRetry_ReverifiesCurrentServiceAndRegistration_NotHistoricalRequester` (PASS_LIBRARY_ONLY)<br>`UntrustedOrMismatchedBinding_RejectsBeforeHandler` (PASS_LIBRARY_ONLY) | Real revocation provider and Owner final business gate for effects after context resolution |
| CTX03-A5 | known rotation只在固定issuer/profile与有界缓存规则内 | 无本次运行子断言 | P03 existing fixed issuer/profile key rotation and bounded cache; no auth/cache changes |
| CTX03-A6 | 响应/日志无token/secret/PII泄露 | `ResolutionFailures_FailClosedWithExplicitStatusAndNoSensitiveBody` (PASS_LIBRARY_ONLY)<br>`AuthorityUnavailable_IsSafeTypedFailure_WithNoFallback` (PASS_LIBRARY_ONLY) | All real host logs/traces/responses and PII audit; adapters must return opaque references, no credentials |
| CTX03-A7 | 原CRM browser与Main service profile没有被通用库误替换 | 无本次运行子断言 | CRM browser and Main service actual profile/runtime preservation; library does not register or replace authentication |
| CTX04-A1 | 同应用每个HTTP/worker/consumer入口各有精确adoption记录 | 无本次运行子断言 | Each actual HTTP/worker/consumer owner must create precise adoption record; see WP02-01 doc inventory |
| CTX04-A2 | 包/注册/启用/运行分层，RecordingStub不算生产 | 无本次运行子断言 | Package/reference/registration/enabled/runtime/business evidence separately required; fixture is not production |
| CTX04-A3 | CRM浏览器、sidecar、Main service身份职责不混 | `NonHttpEntry_RejectsHistoricalHumanOrPublicAsExecutor` (PASS_LIBRARY_ONLY)<br>`EachRetry_ReverifiesCurrentServiceAndRegistration_NotHistoricalRequester` (PASS_LIBRARY_ONLY) | Real CRM browser/sidecar and Main service identity/profile boundaries |
| CTX04-A4 | S6前置内容门之前无Duplicate/IgnoredOutOfOrder成功早退 | 无本次运行子断言 | Consumer S6 content gate ordering before Duplicate/IgnoredOutOfOrder; no consumer business change here |
| CTX04-A5 | 缺共同最终门只关闭相关新效果，合法历史读取保留 | 无本次运行子断言 | Owner missing-final-gate fail-closed new effects with legal historical reads retained |
| CTX04-A6 | 旧新单writer可追溯，不停旧consumer或自动发布包 | 无本次运行子断言 | Actual old/new writer inventory and transition proof; no old consumer stopped or package auto-published |
| CTX04-A7 | typed replay三动作经应用Owner处理，Platform Context不能直接允许重排 | 无本次运行子断言 | Owner typed replay three-action routing; no context permission to reorder |

## PGT-004：缺失或伪造租户输入

原条目原样保存在 acceptance-map.json，status=NOT_RUN。原范围为 CTX01-A2/A4/A6 与 CTX02-A2：无 tenant claim/非法 issuer、A 自称 B、未知 region、body/header schema，以及 tenantless 健康探针。当前库测试证明缺/假/跨租户事实不能进入 fixture 业务回调，HTTP 不读取自报身份，显式健康分支不调用 verifier/authority，失败响应正文为空。

仍需 Main/CRM 实际 resolver、可信目录和 PostgreSQL 数据库接入，记录**取得业务连接前**拒绝、连接获取次数为零、健康探针无业务库调用、无 A1/public fallback、无业务写集、错误不泄露目录。本任务没有数据库，因此不能声称已证明这些连接/写集事实。

## 保留范围

0 条直接分配的 S01 固定向量；整个 247 运行场景不因局部测试自动 PASS。41 条历史静态 REFERENCE_ONLY、缺失 ITER/REL 原件与 WP05 PROPOSED 均保留。JWT 密码学/轮换、CRM/Main profile、S6 内容门、最终授权事务、单 writer、typed replay、数据迁移、真实服务登记与宿主运行记录均由各自责任任务验收。

重建：在 Platform 根目录运行 `python docs/development/evidence/WP02-01/capture_inputs.py --main-repository <Main checkout>`。输入固定到 PROVENANCE 中的 Git SHA；只写本任务目录，不改变 Main 台账/验收状态。
