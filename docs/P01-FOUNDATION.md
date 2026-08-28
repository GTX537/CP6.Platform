# P01 — CP6.Platform Foundation

| 项目 | 内容 |
| --- | --- |
| 决策状态 | Accepted |
| 实现状态 | 在任务分支验证；合并后以远端 `main` CI 为最终证据 |
| 版本 | `0.1.0.0` / package metadata `0.1.0-alpha.0` |
| 仓库 | `https://github.com/GTX537/CP6.Platform`（Private） |
| 本地定位 | `D:\CP6\CP6.Platform` |
| 日期 | 2026-08-27 |

## 1. 目标

P01 为 CP6 的分布式微服务建立唯一、可版本化、可验证的共享平台生产源。它解决仓库位置、包命名、依赖方向、消费源、验证入口和 CI 基线，不提前实现登录、租户、授权、事件或数据库能力。

CRM、ERP、MES 等业务模块只能通过带版本的 NuGet 包消费平台能力。业务仓库不得复制 Platform 源码，也不得把业务语义反向放入 Platform。

## 2. 已冻结的仓库与包结构

```text
CP6.Platform/
├─ src/
│  ├─ CP6.Platform.Contracts/
│  ├─ CP6.Platform.Abstractions/
│  ├─ CP6.Platform.AspNetCore/
│  ├─ CP6.Platform.Messaging/
│  ├─ CP6.Platform.EntityFramework/
│  └─ CP6.Platform.Testing/
├─ tests/
│  ├─ CP6.Platform.UnitTests/
│  └─ CP6.Platform.ArchitectureTests/
├─ eng/verify.ps1
└─ .github/workflows/platform-validation.yml
```

依赖方向固定如下：

```text
Contracts
├─ Abstractions
│  ├─ AspNetCore
│  └─ EntityFramework
├─ Messaging
└─ Testing（只作为最外层消费方测试工具）
```

具体引用集合由架构测试精确约束。`Contracts` 不得依赖其他项目、第三方包或框架引用；所有 `ProjectReference` 必须留在 `src/` 内，且依赖图不得成环。

## 3. P01 范围与明确不做事项

P01 完成：

- `.NET 8`、C# 12、Nullable、warnings-as-errors 和 deterministic build 基线；
- 六个可打包项目的身份、元数据和单向依赖边界；
- 集中测试依赖版本与 NuGet package source mapping；
- 标准 Gate 接口、JSON/JUnit 证据、Windows/Linux CI；
- 本文、贡献规范、测试手册和 ADR；
- 架构自动化测试与可重复打包检查。

P01 不完成：

- 不提供任何运行时公开 API；
- 不发布空包；
- 不创建 GitHub Packages 凭据，不把 PAT 写入仓库；
- 不配置云环境、部署、Registry、数据库或生产 Secret；
- 不实现 P02 关联/审计、P03 可靠事件、P04 跨服务数据、P05 观测、P06 弹性、P07 安全默认值；
- 不在 P01 引入正式 NuGet 包签名。

第一个真实 alpha 包必须随 P02 的真实契约、测试和消费说明一起发布。P02 之前，即使项目具备 `IsPackable=true`，也不发布空包。

## 4. 私有包源与权限

唯一计划包源为 GitHub Packages：

```text
https://nuget.pkg.github.com/GTX537/index.json
```

仓库不要求 GitHub Pro，也不依赖分支保护。个人开发阶段通过任务分支、PR、自动化测试和可审计提交落实规范。

- GitHub Actions 发布/消费时使用工作流的 `GITHUB_TOKEN` 和最小 `packages` 权限；
- 本机消费私有包时，由开发者在用户级 NuGet 配置或安全凭据存储中提供 GitHub classic PAT；
- 仓库级 `NuGet.config` 只保存源地址与映射，不保存用户名、PAT、token 或密码；
- `CP6.Platform.*` 映射到私有源；普通第三方依赖映射到 `nuget.org`。

GitHub Free 的私有 Packages 配额应按官方账单页持续核对。P01 采用 400 MB 预警线：达到时先检查旧 alpha、symbols 和保留策略，不自动删除任何包版本。

参考：

- [GitHub NuGet registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry)
- [GitHub Packages billing](https://docs.github.com/en/billing/concepts/product-billing/github-packages)
- [NuGet package source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping)

## 5. 版本、打包与签名

- 仓库交付版本使用四段 `VERSION`：`0.1.0.0`。
- P01 项目 metadata 使用 `0.1.0-alpha.0`，仅用于验证 pack 可重复性，不发布。
- 正式包版本从首个真实能力（P02）开始，由当次任务决定并写入变更记录。
- 每个包包含 repository metadata、portable symbols，并启用 deterministic build。
- P01 的完整性边界是：私有源权限、不可重用版本、CI 证据与可重复打包。
- NuGet 正式签名、证书托管、验证策略和轮换流程统一推迟到 P10 发布治理；任何提前签名方案都必须单独 ADR，不能静默漂移。

## 6. 标准验证契约

统一入口：

```powershell
pwsh ./eng/verify.ps1 -Gate <Gate> [-Profile <profile>]
```

必备 Gate：`Format`、`Build`、`Unit`、`Integration`、`Contract`、`Security`、`E2E`、`Performance`、`Migration`。

P01 中没有运行时或数据资产的 Gate 返回 `NotApplicable` 和明确原因；失败返回非零退出码。每次调用都输出：

- `artifacts/verify/<gate>/summary.json`
- `artifacts/verify/<gate>/results.junit.xml`

CI 在 `ubuntu-latest` 和 `windows-latest` 上使用同一脚本，不维护另一套隐藏命令。CI 对成功和失败运行都上传按操作系统命名的验证证据，并保留 7 天。Security Gate 以 `NuGetAuditMode=all` 覆盖直接和传递依赖，漏洞警告按 errors 失败关闭。

## 7. P01 验收条件

只有同时满足以下条件，CRM 的定位记录才可标记为 `Frozen / Producer Ready`：

1. 私有远端仓库存在且默认分支为 `main`；
2. 六个包项目、两个测试项目和依赖方向通过架构测试；
3. Format、Build、Unit、Contract、Security 在本地通过；
4. Integration、E2E、Performance、Migration 明确为 `NotApplicable`；
5. 任务 PR 合并到远端 `main`；
6. 合并后的 Windows/Linux CI 全部通过；
7. 没有发布任何 P01 空包。

`Producer Ready` 只表示 Platform 生产仓库和生产流程已就绪，不表示 CRM 已消费包，也不表示 P02 及后续能力存在。

## 8. 后续顺序

P01 完成后进入 P02。P02 首先定义跨服务关联标识与审计契约，交付第一个真实 alpha 包和最小消费者证明。P02+ 在有代码、测试、包版本和消费证据前继续保持 `Absent`。
