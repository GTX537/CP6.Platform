# ADR-P01: Private package source and signing boundary

- Status: Accepted
- Date: 2026-08-27

## Context

CP6 的 CRM、ERP、MES 将作为独立微服务演进，需要共享少量稳定契约和基础设施，但不能依赖源码复制或业务仓库之间的直接项目引用。当前只有一名开发者，不购买 GitHub Pro，也不需要复杂保护规则。

## Decision

1. `GTX537/CP6.Platform` 是共享平台包的唯一生产仓库，仓库保持 Private。
2. 私有 NuGet 包统一发布到 `https://nuget.pkg.github.com/GTX537/index.json`。
3. CI 使用 GitHub Actions `GITHUB_TOKEN`；本机凭据只保存在用户级安全配置，不进入仓库。
4. 仓库 `NuGet.config` 使用 package source mapping，将 `CP6.Platform.*` 指向 GitHub Packages，其余包指向 nuget.org。
5. P01 不发布空包。P02 交付首个真实 alpha 能力时才发布第一个包版本。
6. P01 不实施正式 NuGet 包签名。签名证书、信任、轮换和强制校验作为 P10 发布治理的一部分统一设计。
7. 个人开发仍使用任务分支、PR 和 Windows/Linux CI，但不要求 GitHub Pro 或分支保护。

## Consequences

- 业务仓库可以通过明确版本消费共享能力，Platform 与业务模块保持独立发布。
- 本机恢复私有包需要开发者自己配置具有最小 `read:packages` 权限的凭据。
- P01 的包项目可用于构建和可重复性验证，但在没有真实 API 前不能上传。
- 到 P10 之前，包完整性依赖私有源权限、不可重用版本、仓库/commit 元数据和 CI 证据；不能宣称已完成正式签名治理。
- 如果未来更换 Registry，需要新的 ADR、迁移期、回退方案和唯一发布权威，不能让两个源分别构建并宣称同一版本。
