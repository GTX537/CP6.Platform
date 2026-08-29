# P07-S02 — 不可变包发布

| 项目 | 值 |
| --- | --- |
| 公开里程碑 | `P07-S02` |
| 状态 | Published / immutable evidence verified / CRM fixed-version consumption passed |
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

## 完成证据

- 实现基线：Platform PR #15 的 `head@229a4e814ced98595c0a64ef393d3d1a7cbbbbbf` 在 run 33260845830 通过四组门禁，合并为 `main@8167deac14bf82b0f576e73bf7c2e202049d1896`；main run 33261055327 attempt 2 全部成功。
- 发布基线：Platform PR #16 的 `head@720ed9f1345e30090c346990eeecec0d68938580` 在 run 33262266953 通过四组门禁，合并为 `main@329bf8ee82091de569cb80f1e83fc5d518f74068`；main run 33262410890 与 exact-main publish run 33262569274 全部成功。
- 发布 artifact：ID 9717721544，名称 `p07-alpha-329bf8ee82091de569cb80f1e83fc5d518f74068`，digest `bf6487e55d8345b1dfbe39cedc2afcb3e365c9bcdd36bbec17841996cd4e88a0`。
- 不可变包：
  - `CP6.Platform.Contracts 0.7.0-alpha.1`：`609c4f562858be6cfe45fc2d51c9eaeceb50a392ce28135a4a9ea644416054d9`
  - `CP6.Platform.Abstractions 0.7.0-alpha.1`：`7149940ddb817145fe615a51c8e517fe1915516bf9188b6e2f084e21d7738479`
  - `CP6.Platform.AspNetCore 0.7.0-alpha.1`：`85f0742253ebed8adaad2fcf63f2166545e5c4cf35adf739eb87c1eed63fd010`
  - `CP6.Platform.EntityFramework 0.7.0-alpha.1`：`a16de9d73df5dda91c4be9e667c4352fad354c5425fb207ae922148d864b7a0e`
  - `CP6.Platform.Messaging 0.7.0-alpha.1`：`b385cebe84ebd6dafac723724b273a8660999042bc95f3e6dd88af433cbbd7fd`
- CRM 消费：PR #31 `head@2346325f8e18773fa20634b28422cf199df85669` 的 run 33264347561 和合并后 `main@02f7078de6a67e7f3fded6df6a84b9f6fb712a84` run 33264676796 通过固定版本恢复、11/11 Gateway loopback 契约及既有 CRM/SQL 门禁。
- 冻结边界：CRM PR #32 `head@86f8d0401fe53e56994ee8cd7b3644f07d259a71` 的 run 33265394681 与最终 `main@467d95e46625d4db0bb7aa0932aff5464f64a01b` run 33265702772 通过，把机器 locator 固定为 `Frozen / Consumable`；不登记运行时 Gateway、不启用 CRM route，也不声明 P09 网络隔离。
- 公共同步：`GTX537/CP6` PR #71 `head@6ee481d97c3c5b4454bbc8b910a6b651cc94eebf` 的 PR runs 33266086473、33266086476、33266086486、33266086498、33266086503、33266086514 全部通过，合并为 `main@47263a498caadcb545092ca617e3d86633e9bea5`；exact-main runs 33266594792、33266594799、33266594815、33266594818、33266594824 全部成功。

## 失败与前向修复

- PR/main 门禁失败：不得触发发布，使用新提交前向修复。
- SHA 不一致：workflow 必须在 push 前停止，从新的当前 `main` 重新审批。
- 包集、assembly、Gateway E2E、回归或 hash 不一致：不得发布，禁止放宽脚本或手工补包。
- 部分 push 成功：保留不可变证据，不重用 `0.7.0-alpha.1`；评审后以更高唯一版本前滚。

S02 不发布容器、不创建 Gateway/Dapr/Kafka/SQL Server 部署资源、不选择 Registry，也不部署环境。C01/C02/CRM03、真实登录、CRM route 和 P09 NetworkPolicy 仍是独立任务。
