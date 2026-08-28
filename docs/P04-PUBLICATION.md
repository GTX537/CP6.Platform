# P04-S02 — 不可变包发布

| 项目 | 值 |
| --- | --- |
| 公开里程碑 | `P04-S02` |
| 状态 | Published / immutable evidence verified / CRM fixed-version consumption passed |
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

## 完成证据

- 发布基线：`CP6.Platform main@2c4c601228d81b300659b7773748da2e995ce433`；发布 workflow run 33167927567 从精确 main SHA 完成全部 release gates、pack、push 与证据上传。
- 发布 artifact：ID 9684391334；SHA-256 `07eae751d6288cf8f8d81561ae77ac4ab452d62610966a9c69cad995a05bea3e`。
- 不可变包：
  - `CP6.Platform.Contracts 0.4.0-alpha.1`：`b41b6f65507fc1c1db9db7c6213b793787af17e53c6f3d5c8debac7a7606b278`
  - `CP6.Platform.Abstractions 0.4.0-alpha.1`：`d867e6ad43355113ab29a775e0801f11643c0f60ae4c65e58fa41d9646423139`
  - `CP6.Platform.AspNetCore 0.4.0-alpha.1`：`8749825d6cfa0d899ef2ab21421818bf1bfdc36e0a31b4607b60de71bda7c5c5`
  - `CP6.Platform.Messaging 0.4.0-alpha.1`：`50fd23395f49d14ec22619cdddce8006f2b5ec33c465787496f5c5582a74d762`
- CRM 消费：PR #25 run 33169553326 attempt 2 与合并后 `main@bdc298dc38196fefa4613927cb48dfb6c41f1a66` run 33170491020 均通过 GitHub Packages 远端固定版本恢复、bundle 消费和完整 CRM 门禁。
- 冻结边界：CRM PR #26 run 33171439705 与合并后 `main@2a728411c6becd437bb0e1f7f4ead680a0947c52` run 33171913476 通过，并把机器 locator 固定为 `Frozen / Consumable`；不启用运行时订阅、P05/P06 或业务事件。

## 失败与前向修复

- PR/main 门禁失败：不触发发布，在新的任务提交中前向修复。
- SHA 不一致：workflow 必须在 push 前停止，从新的当前 `main` 重新审批。
- 包集、assembly、bundle 内容或 hash 不一致：不发布，禁止放宽脚本或手工补上丢失文件。
- 部分 push 成功：保留不可变证据，不重用 `0.4.0-alpha.1`；评审后以更高唯一版本前滚。

S02 不发布容器、不选择 GHCR/ACR、不部署环境。CP6 应用的 Registry 与发布权威仍受 `docs/devops/AZURE-PIPELINES-PLAN.md` 的 Phase 2 门禁约束。
