# Pkcs 8.0.2 审计修复（Issue #75）

基线：`3b75dde64f702df122a46a7b49c692a53a880ff1`。仅将中央包配置中的
`System.Security.Cryptography.Pkcs` 从 `8.0.1` 更新为同系列补丁 `8.0.2`；
保留 NuGet 审计及 warnings-as-errors，不修改正式包版本、签名或信任策略。

根因：Microsoft 的 [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7)
及 [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx)
均将 8.0.0–8.0.1 列为受影响版本，8.0.2 为修复版本。
原始标准恢复失败保留在 WP02-02 候选
`fcb43d3077f2b4c503929307410b1e3f2831712e` 的
`docs/development/evidence/WP02-02/formal-registration/implementation/`，不覆盖旧失败。

2026-10-15 本地 SDK 8.0.424 验证：

- `dotnet restore tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --force-evaluate -p:NuGetAudit=true -p:NuGetAuditMode=all`：PASS，解析到 Pkcs 8.0.2，无警告豁免。见 [restore.log](restore.log)。
- `dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~FormalPackageVerifierTests&FullyQualifiedName!~Formal_verifier_accepts_one_real_RFC3161_timestamp' --logger 'trx;LogFileName=verifier.trx' --results-directory docs/development/evidence/bug-75-pkcs`：4/4 PASS，0 skip。见 [verifier.log](verifier.log)、[verifier.trx](verifier.trx)。
- 集中审查无阻断：两个直接消费者为非打包工具 ReleaseTool、Inspector；七个正式库未增加依赖，无需要更新的受版本控制锁文件。未受影响的历史验证不重跑。

Inspector 未独立验证，实际签名、RFC3161、正式发布和消费者业务验收仍未执行。
未运行或取消 Actions，未改变触发器、迁库或部署。Issue 在正常 PR 合并且核对远端 main 后关闭。
