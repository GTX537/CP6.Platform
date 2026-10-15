# WP02-02 第八轮协调补充：登记实现可以交付

本报告承接原 [DELIVERY](../DELIVERY.md)；原始失败和诊断范围保留。
原候选 `fcb43d3077f2b4c503929307410b1e3f2831712e` 已整合到
`568c844683b3df931f1b6de921e0be5113fa50b9` 主线后的独立协调分支。
本轮只解除登记源码的交付阻塞，完整 Main WP02-02 仍为 `BLOCKED / NOT_CHECKED`。

## 修复与审查

- Pkcs 恢复失败由独立 [PR #76](https://github.com/GTX537/CP6.Platform/pull/76) 修复，
  已核远端 main 并关闭 [Issue #75](https://github.com/GTX537/CP6.Platform/issues/75)。
  [审计恢复与四项验证证据](../../../bug-75-pkcs/README.md)独立保留，不关闭审计或豁免警告。
- 协调接管 `P10PackageTests.cs` 两条准确断言：正式 schema 接受已登记 `0.10.3`，
  同时保留未知 `0.10.4` 的拒绝；包资产仅新增已有的
  `contracts/release/capabilities/v1/` 前缀，未放宽为全部 contracts。
- 一次任务级集中审查核对原候选与最终主线差异、四版本读取、仅 `.3` 写入、
  ctx 能力及旧版/历史记录兼容。局部修正经定向复查，无剩余阻断。
  本报告不把任务范围外的正式发布或真实消费写作已通过。

## 本次实际验证

2026-10-15，Windows SDK 8.0.424，PATH 同样指向该 SDK：

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=true -p:NuGetAuditMode=all --filter 'FullyQualifiedName~P10FormalRegistrationTests&FullyQualifiedName!~Actual_task_local|FullyQualifiedName~FormalPackagePublicationTests|FullyQualifiedName~ReleaseSchemaAssetTests|FullyQualifiedName~P10FormalWorkflowContractTests|FullyQualifiedName~CapabilitiesV1|FullyQualifiedName~Packaged_formal_publication_Schema_accepts_only_coherent_registered_versions_and_feed_identities|FullyQualifiedName~Release_package_contains_only_dll_xml_readme_and_release_contract_assets' --logger 'trx;LogFileName=targeted.trx' --results-directory docs/development/evidence/WP02-02/formal-registration/coordination
```

**177/177 PASS、0 skip**，无警告豁免，实际恢复、编译及两条原失败断言均通过。
见 [targeted.log](targeted.log)、[targeted.trx](targeted.trx)。未重跑完整 Release suite，
没有将原 `353 PASS / 2 FAIL` 改为全量通过。177 项包含原定向集合与后续相关回归，
不与历史 171 或 6 相加。`git diff --check` 通过。

原候选的实际 unsigned TaskLocal 包校验、Architecture 98、工具与私有宿主结果仅按
[原报告](../DELIVERY.md)适用范围引用：源码/合同的原结果可复用；依赖已改变的旧宿主
不视为最终 producer 的消费验证。未重建全部七包或重跑跨仓服务。

## 交付与未执行范围

生产者最终 SHA 由 COORD 在正常 PR 合并、远端 main 包含性核对后冻结到 Main 台账及 Handoff。
历史 `formal-package-plan.json` 的 null 记录保留；源码身份冻结不代表制品已发布。
七个正式包、真实 Main/Owner/PostgreSQL 与原 17 PGT 仍未验收。
三项签名测试和 Current 在线信任仍 `NOT_RUN`。

普通 push/PR 路径已核：正式工作流仅三处版本文字/判断更新，事件未改；
RFC3161 自动 PR 两条窄路径未命中。无 Actions 运行或取消、触发配置修改、正式签名/发包、
迁库或部署。按用户精简规则，不追加全仓检查、重复审查或逐文件回执。
