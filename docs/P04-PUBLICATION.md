# P04-S02 — 不可变包发布

| 项目 | 值 |
| --- | --- |
| 公开里程碑 | `P04-S02` |
| 状态 | Release automation candidate / Awaiting merged-main publication |
| 前置 | P04-S01 已合并到 Platform `main`；Windows/Linux 验证通过 |
| DRI | Platform Owner（BUBAO.GAO） |
| Reviewer | Security、SRE、CRM Owner；单人流程仍不得跳过 GitHub 门禁与不可变证据 |
| 输入 | Platform 当前 `main` 完整 SHA、`0.4.0-alpha.1`、P04 bundle |
| 输出 | GitHub Packages 中四个固定版本 NuGet 包、`sha256.json`、全部 verify 证据与 workflow run ID |

## 可观察行为

`publish-alpha.yml` 只允许从 `main` 手动触发，要求输入完整 `expected_commit`。检出 SHA、当前 `origin/main` 和批准 SHA 必须三者完全一致，否则在 pack/push 前失败。发布前重跑 Format、Build、Unit、Integration、Contract 和 Security。

候选集合精确为 `CP6.Platform.Contracts`、`CP6.Platform.Abstractions`、`CP6.Platform.AspNetCore` 和 `CP6.Platform.Messaging` 的 `0.4.0-alpha.1`。Messaging 包必须包含 bundle 索引、Schema 和五个示例。发布命令不使用 `--skip-duplicate`；相同版本的重放必须失败，不得覆盖或冒充新候选。

## 命令与验收

```powershell
pwsh ./eng/verify.ps1 -Gate Format -Profile release
pwsh ./eng/verify.ps1 -Gate Build -Profile release
pwsh ./eng/verify.ps1 -Gate Unit -Profile release
pwsh ./eng/verify.ps1 -Gate Integration -Profile release
pwsh ./eng/verify.ps1 -Gate Contract -Profile release
pwsh ./eng/verify.ps1 -Gate Security -Profile release
pwsh ./eng/pack-release.ps1 -OutputDirectory artifacts/release -PackageVersion 0.4.0-alpha.1

# 仅在本任务 PR 已合并且 main CI 通过后：
gh workflow run publish-alpha.yml --ref main -f expected_commit=<full-main-sha>
```

DoD：

1. S02 PR 的 Windows/Linux 门禁通过并合并到 `main`；
2. publish run 检出的 SHA 精确等于触发时的远端 `main`；
3. 四个普通 `.nupkg` 发布成功，并保存包级 SHA-256 与验证 artifacts；
4. 下载证据与发布包名、版本、数量和 hash 一致；
5. 在 P04-S03 中由 CRM 从 GitHub Packages 固定 `0.4.0-alpha.1` 恢复并验证同一 bundle。

## 失败与前向修复

- PR/main 门禁失败：不触发发布，在新的任务提交中前向修复。
- SHA 不一致：workflow 必须在 push 前停止，从新的当前 `main` 重新审批。
- 包集、assembly、bundle 内容或 hash 不一致：不发布，禁止放宽脚本或手工补上丢失文件。
- 部分 push 成功：保留不可变证据，不重用 `0.4.0-alpha.1`；评审后以更高唯一版本前滚。

S02 不发布容器、不选择 GHCR/ACR、不部署环境。CP6 应用的 Registry 与发布权威仍受 `docs/devops/AZURE-PIPELINES-PLAN.md` 的 Phase 2 门禁约束。
