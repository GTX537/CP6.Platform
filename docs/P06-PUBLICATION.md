# P06-S02 — 不可变包发布

| 项目 | 值 |
| --- | --- |
| 公开里程碑 | `P06-S02` |
| 状态 | Published / immutable evidence verified / CRM fixed-version consumption passed |
| 前置 | P06-S01 已合并到 Platform `main@aa4cce820c2bd0104ed461d92e2ded481ccc8ba1`；main run 33241821365 全通过 |
| 输入 | Platform 当前 `main` 完整 SHA、`0.6.0-alpha.1`、P04/P05 contract 与 P06 SQL semantics |
| 输出 | GitHub Packages 中五个固定版本 NuGet 包、`sha256.json`、全部 verify、Dapr/Kafka 与 SQL Server 证据 |

## 发布约束

`publish-alpha.yml` 只允许从 `main` 手动触发，并要求完整 `expected_commit`。检出 SHA、远端 `origin/main` 与批准 SHA 必须完全一致，否则必须在 pack/push 前失败。

发布前重跑 Format、Build、Unit、Contract、Security，并分别以 `p05-real` 和 `p06-real` profile 运行真实 Dapr/Kafka 与 SQL Server 集成。候选集合只允许下列五个 `0.6.0-alpha.1` 包：

- `CP6.Platform.Contracts`
- `CP6.Platform.Abstractions`
- `CP6.Platform.AspNetCore`
- `CP6.Platform.Messaging`
- `CP6.Platform.EntityFramework`

发布不使用 `--skip-duplicate`。同版本重放必须失败，不能覆盖既有包，也不能把部分成功伪装成完整候选。逐包 SHA-256 与 workflow artifact digest 必须保存并用于 CRM 消费证据绑定。

## 验收命令

```powershell
pwsh ./eng/verify.ps1 -Gate Format -Profile release
pwsh ./eng/verify.ps1 -Gate Build -Profile release
pwsh ./eng/verify.ps1 -Gate Unit -Profile release
pwsh ./eng/verify.ps1 -Gate Integration -Profile p05-real
pwsh ./eng/verify.ps1 -Gate Integration -Profile p06-real
pwsh ./eng/verify.ps1 -Gate Contract -Profile release
pwsh ./eng/verify.ps1 -Gate Security -Profile release
pwsh ./eng/pack-release.ps1 -OutputDirectory artifacts/release -PackageVersion 0.6.0-alpha.1

# 仅在本任务 PR 合并且 main CI 通过后：
gh workflow run publish-alpha.yml --ref main -f expected_commit=<full-main-sha>
```

## 完成定义

1. S02 PR 的 Windows、Linux、真实 Dapr/Kafka 与真实 SQL Server 门禁通过并合并到 `main`；
2. 发布 run 检出的 SHA 精确等于触发时的远端 `main`；
3. 五个普通 `.nupkg` 发布成功，包名、版本、数量和 SHA-256 与 artifact 一致；
4. CRM 从 GitHub Packages 固定 `0.6.0-alpha.1` 恢复，并用真实 SQL Server 验证重复、冲突、乱序和 handler 失败均无业务副作用；
5. Platform、CRM 与公共 CP6 记忆均绑定精确 commit、PR 和 run 证据后，P06 才可标为 `Frozen / Consumable`。

## 完成证据

- 发布基线：`CP6.Platform main@3b1669a05f9b265f9b3fb14ade4d656018cbf6b5`；实现 main run 33241821365、发布 main run 33242125202 和 exact-main publish run 33242264497 均成功，后者从精确 SHA 重跑 release、真实 Dapr/Kafka 与真实 SQL Server 门禁。
- 发布 artifact：ID 9711742920；SHA-256 `44431d7f359ea524ba9dc438f6f70d24bf34be69411729a2fe1953e3039b3b86`。
- 不可变包：
  - `CP6.Platform.Contracts 0.6.0-alpha.1`：`acb42d617635ed6ba484edf1281a6c3a049c209d0c861015e5a9e269141722a4`
  - `CP6.Platform.Abstractions 0.6.0-alpha.1`：`004ff6d528e7d15a2887df51f42035105804988d49e387d87aea6f0555e4b759`
  - `CP6.Platform.AspNetCore 0.6.0-alpha.1`：`1104e5319195a2ff8a59a4cf3893766fa959e8733a10486cf260998c61c020fb`
  - `CP6.Platform.EntityFramework 0.6.0-alpha.1`：`63491b51b6c0302b0ec662341764181665952c36788f1ef4dcd1424ef75777e7`
  - `CP6.Platform.Messaging 0.6.0-alpha.1`：`5bcbb2bec969ac463876b84c87c36d6a93b316642943ab5f3ded2103d8c6c410`
- CRM 消费：PR #29 run 33243227124 attempt 3 和合并后 `main@910804f5e7fa02569da958ae325997e10c0ffbc0` run 33244344319 通过固定版本恢复、真实 SQL Server、28/28 .NET、39/39 Web、production build 与 3/3 Chromium smoke。
- 冻结边界：CRM PR #30 run 33244749522 与最终 `main@744ca5d9d06db4470d18a4d8ce3ecfbae42f1d2c` run 33245027773 通过，把机器 locator 固定为 `Frozen / Consumable`；不启用 Worker、运行时订阅或业务事件。
- 公共同步：`GTX537/CP6` PR #69 的六组 PR runs 全通过并合并为 `main@d049ed37c5db4dca38bdfb171f9bc8a5e76f61f1`；exact-main runs 33246016907、33246016908、33246016913、33246016923、33246016953 全部成功。

## 失败与前向修复

- PR/main 门禁失败：不得触发发布，使用新提交前向修复。
- SHA 不一致：workflow 必须在 push 前停止，从新的当前 `main` 重新审批。
- 包集、assembly、真实集成或 hash 不一致：不得发布，禁止放宽脚本或手工补包。
- 部分 push 成功：保留不可变证据，不重用 `0.6.0-alpha.1`；评审后以更高唯一版本前滚。

S02 不发布容器、不创建 Dapr/Kafka/SQL Server 部署资源、不选择 Registry，也不部署任何环境。CRM Worker、业务事件和 migrations 仍是独立消费方任务。
