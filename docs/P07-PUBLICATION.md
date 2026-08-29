# P07-S02 — 不可变包发布

| 项目 | 值 |
| --- | --- |
| 公开里程碑 | `P07-S02` |
| 状态 | Ready for immutable publication |
| 前置 | P07-S01 已合并到 Platform `main@8167deac14bf82b0f576e73bf7c2e202049d1896`；main run 33261055327 attempt 2 四组门禁全通过 |
| 输入 | Platform 当前 `main` 完整 SHA、`0.7.0-alpha.1`、P03/P07 gateway contracts 与 P04–P06 回归 |
| 输出 | GitHub Packages 中五个固定版本 NuGet 包、`sha256.json`、全部 verify、Gateway E2E、Dapr/Kafka 与 SQL Server 回归证据 |

## 发布约束

`publish-alpha.yml` 只允许从 `main` 手动触发，并要求完整 `expected_commit`。检出 SHA、远端 `origin/main` 与批准 SHA 必须完全一致，否则必须在 pack/push 前失败。

发布前重跑 Format、Build、Unit、Gateway E2E、Contract、Security，并分别以 `p05-real` 和 `p06-real` profile 运行真实 Dapr/Kafka 与 SQL Server 回归。候选集合只允许五个 `0.7.0-alpha.1` 包：Contracts、Abstractions、AspNetCore、Messaging、EntityFramework。

发布不使用 `--skip-duplicate`。同版本重放必须失败，不能覆盖既有包，也不能把部分成功伪装成完整候选。逐包 SHA-256 与 workflow artifact digest 必须保存并用于 CRM 消费证据绑定。

## 验收命令

```powershell
pwsh ./eng/verify.ps1 -Gate Format -Profile release
pwsh ./eng/verify.ps1 -Gate Build -Profile release
pwsh ./eng/verify.ps1 -Gate Unit -Profile release
pwsh ./eng/verify.ps1 -Gate Integration -Profile p05-real
pwsh ./eng/verify.ps1 -Gate Integration -Profile p06-real
pwsh ./eng/verify.ps1 -Gate E2E -Profile release
pwsh ./eng/verify.ps1 -Gate Contract -Profile release
pwsh ./eng/verify.ps1 -Gate Security -Profile release
pwsh ./eng/pack-release.ps1 -OutputDirectory artifacts/release -PackageVersion 0.7.0-alpha.1

# 仅在本任务 PR 合并且 main CI 通过后：
gh workflow run publish-alpha.yml --ref main -f expected_commit=<full-main-sha>
```

## 完成定义

1. S02 PR 的 Windows、Linux、真实 Dapr/Kafka 与真实 SQL Server 门禁通过并合并到 `main`；
2. 发布 run 检出的 SHA 精确等于触发时的远端 `main`；
3. 五个普通 `.nupkg` 发布成功，包名、版本、数量和 SHA-256 与 artifact 一致；
4. CRM 从 GitHub Packages 固定 `0.7.0-alpha.1` 恢复，并验证 route、伪造 identity header、429 与后端独立认证边界；
5. Platform、CRM 与公共 CP6 记忆绑定精确 commit、PR 和 run 证据后，P07 才可标为 `Frozen / Consumable`。

## 失败与前向修复

- PR/main 门禁失败：不得触发发布，使用新提交前向修复。
- SHA 不一致：workflow 必须在 push 前停止，从新的当前 `main` 重新审批。
- 包集、assembly、Gateway E2E、回归或 hash 不一致：不得发布，禁止放宽脚本或手工补包。
- 部分 push 成功：保留不可变证据，不重用 `0.7.0-alpha.1`；评审后以更高唯一版本前滚。

S02 不发布容器、不创建 Gateway/Dapr/Kafka/SQL Server 部署资源、不选择 Registry，也不部署环境。C01/C02/CRM03、真实登录、CRM route 和 P09 NetworkPolicy 仍是独立任务。
