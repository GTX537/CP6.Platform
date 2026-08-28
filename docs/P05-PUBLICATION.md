# P05-S02 — 不可变包发布

| 项目 | 值 |
| --- | --- |
| 公开里程碑 | `P05-S02` |
| 状态 | Ready for exact-main publication；等待发布与 CRM 消费证据 |
| 前置 | P05-S01 已合并到 Platform `main`；Windows、Linux、真实 Dapr/Kafka 验证通过 |
| 输入 | Platform 当前 `main` 完整 SHA、`0.5.0-alpha.1`、P04 bundle 与 P05 transport contract |
| 输出 | GitHub Packages 中四个固定版本 NuGet 包、`sha256.json`、全部 verify 与真实集成证据 |

## 发布约束

`publish-alpha.yml` 只允许从 `main` 手动触发，并要求完整 `expected_commit`。检出 SHA、远端 `origin/main` 与批准 SHA 必须完全一致，否则必须在 pack/push 前失败。

发布前重跑 Format、Build、Unit、Contract、Security，并以 `p05-real` profile 再次运行 ASP.NET 与真实 Dapr `1.18.2` / Kafka `4.3.1` 集成。候选集合只允许下列四个 `0.5.0-alpha.1` 包：

- `CP6.Platform.Contracts`
- `CP6.Platform.Abstractions`
- `CP6.Platform.AspNetCore`
- `CP6.Platform.Messaging`

发布不使用 `--skip-duplicate`。同版本重放必须失败，不能覆盖既有包，也不能把部分成功伪装成完整候选。逐包 SHA-256 与 workflow artifact digest 必须保存并用于 CRM 消费证据绑定。

## 验收命令

```powershell
pwsh ./eng/verify.ps1 -Gate Format -Profile release
pwsh ./eng/verify.ps1 -Gate Build -Profile release
pwsh ./eng/verify.ps1 -Gate Unit -Profile release
pwsh ./eng/verify.ps1 -Gate Integration -Profile p05-real
pwsh ./eng/verify.ps1 -Gate Contract -Profile release
pwsh ./eng/verify.ps1 -Gate Security -Profile release
pwsh ./eng/pack-release.ps1 -OutputDirectory artifacts/release -PackageVersion 0.5.0-alpha.1

# 仅在本任务 PR 合并且 main CI 通过后：
gh workflow run publish-alpha.yml --ref main -f expected_commit=<full-main-sha>
```

## 完成定义

1. S02 PR 的 Windows、Linux 与真实 Dapr/Kafka 门禁通过并合并到 `main`；
2. 发布 run 检出的 SHA 精确等于触发时的远端 `main`；
3. 四个普通 `.nupkg` 发布成功，包名、版本、数量和 SHA-256 与 artifact 一致；
4. CRM 从 GitHub Packages 固定 `0.5.0-alpha.1` 恢复，并在无运行时订阅、无业务副作用的边界内验证 P05 约定；
5. Platform、CRM 与公共 CP6 记忆均绑定精确提交、PR 和 run 证据后，P05 才可标为 `Frozen / Consumable`。

## 失败与前向修复

- PR/main 门禁失败：不得触发发布，使用新提交前向修复。
- SHA 不一致：workflow 必须在 push 前停止，从新的当前 `main` 重新审批。
- 包集、assembly、bundle、真实集成或 hash 不一致：不得发布，禁止放宽脚本或手工补包。
- 部分 push 成功：保留不可变证据，不重用 `0.5.0-alpha.1`；评审后以更高唯一版本前滚。

S02 不发布容器、不创建 Dapr 部署资源、不选择 Registry，也不部署任何环境。P06 持久化语义与 P09 部署资产仍是独立里程碑。
