# 本地验证的失败与边界

## 必需 Release 命令：恢复失败

第一次按执行包运行 ReleaseTests 的实际命令在测试开始前失败，原日志为
`implementation/red.log`。只读 `Directory.Packages.props` 中既有
`System.Security.Cryptography.Pkcs 8.0.1` 被 NuGet 报告 NU1902
（GHSA-2fmv-rv2c-59x7）和 NU1903（GHSA-846f-f764-76mx），
现有 TreatWarningsAsErrors 将其提升为错误。没有修改依赖、项目或审计规则。

已发送协调 QUESTION，固定 Handoff 提交
`a602023b87148543ae512ef957bd284cc1c4a8c8`，路径
`handoffs/CP6-DEV-20261012/tasks/WP02-02/messages/20261015T054310Z-wave8-release-gate-dependency-question/MESSAGE.md`。
需要串行依赖修复窗口或后继明确决定；本候选不能因诊断通过而合并。

后续测试仅使用命令级 `WarningsNotAsErrors=NU1902,NU1903` 将这些已知警告
保持为可见警告，标注 DIAGNOSTIC_ONLY。不关闭 NuGetAudit、不使用 NoWarn，
不把该结果称为标准命令或安全门禁通过。实际参数与转义以各命令记录为准。

## 无授权的签名测试：NOT_RUN

现有 `FormalPackageVerifierTests.Formal_verifier_accepts_one_real_RFC3161_timestamp_and_rejects_identity_and_integrity_mutations`
会打包稳定 0.10.0、生成临时签名 PFX、执行 `dotnet nuget sign` 并访问真实 DigiCert TSA。
另有 `P10PackageTests.Test_package_set_has_exact_seven_ids_one_version_one_source_and_test_only_trust`
和 `P10PackageTests.Injected_signing_failure_removes_private_material` 会执行临时 TestOnly 签名。
本轮明确禁止稳定签名包和签名，故诊断过滤这三项测试，保留原测试代码。
这不是跳过后宣称全量通过：该测试和 Current 在线验证明确 NOT_RUN。
其余 API、schema、workflow、离线信任边界验证的适用范围见实际测试结果。

## 窗口外打包测试：两项实际 FAIL

完整安全诊断实际执行 355 项，353 PASS、2 FAIL、0 skip；过滤的三项不计入这 355 项。
两项失败均在未授予写权限的 `tests/CP6.Platform.ReleaseTests/P10PackageTests.cs`：
`Packaged_formal_publication_Schema_accepts_only_coherent_registered_versions_and_feed_identities`
仍断言 0.10.3 不被允许；`Release_package_contains_only_dll_xml_readme_and_release_contract_assets`
未允许已存在的 `contracts/release/capabilities/v1/` 资产路径。
原文件未改，也未以筛掉它们来声称完整诊断通过。

追加精确文件窗口的 QUESTION 已发布于 Handoff
`14f86eb13a5061b822e625e52b2705f9d4d19c19`，路径
`handoffs/CP6-DEV-20261012/tasks/WP02-02/messages/20261015T054746Z-wave8-exact-test-scope-question/MESSAGE.md`。

## 私有兼容诊断首次失败：NU1008

owner `477a039df2ee4a95a42bcbb2aa143d76` 保留在
`compatibility/attempt1/`。两个 Main 入口恢复失败，私有 probe 构建成功但未启动。
实际 MSBuild 属性观测 `parent-import-diagnostic.json` 表明 Main 快照向上导入了
工作树根 Platform 的中央包配置。Main 自身没有该文件；这不是产品依赖的变化。

私有 runner 在新 owner 根写入且逐次核验惰性 `<Project />` 包配置边界，
阻断父级查找；快照及原 NuGet 信任配置不改动，Platform/probe 自身中央包配置仍有效。
新增真实 MSBuild 回归先 RED 后 GREEN，最终工具测试 30 项通过。
后续重验使用新 owner `e19a9115d0c64d069cd5126ddc4cff59`，不清除或复用失败 owner。

新 owner 恢复日志中 Main 原有 System.Security.Cryptography.Xml 8.0.4 漏洞警告
原样保留；兼容诊断不构成安全、签名、生产发布或业务验收。
