# WP02-01 交付报告

状态：Platform 限定窗口的源码实现、本地必需验证和一次集中审查已完成，交 COORD 串行集成。**不声明远端 main 已包含、不登记任务 DONE，不声明 PLT-CTX-01 或真实宿主验收完成。** 候选推送后的完整 SHA 与通信固定提交由本任务 Handoff DELIVERY 消息记录。

## 固定身份

- 实现/测试/主要证据提交：`00e39bfd6a69fe2adcd60387850005d538d2c8db`。
- 任务分支：`codex/wp02-01-trusted-context-20261012`，仓库 `GTX537/CP6.Platform`。
- 实现基线、已确认 origin/main：`dc4c0ab216974339ab3c2cebaaf4bb92c9e9b351`。
- 独立审查范围：上述基线→候选 tree `83bf1296739c0788b9fbbd16a29dc5adc7c19193`；后继只补任务证据/交付文档，源码、测试和依赖未变。
- Main 输入：`e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47`；分配/窗口：`e20740e37e20bc0210e0b32c5670c2bce962002a`。
- START 已发布并核对远端 Git SHA 和原字节：Handoff `bbfebdd46d97fee3ac6cf10dbbf6e937220e0c0b`，`handoffs/CP6-DEV-20261012/tasks/WP02-01/messages/20261013T051903Z-c-start/MESSAGE.md`。
- S01 固定包 `CP6-S01-CONTRACT-INPUTS/1.0.0`，直接分配向量 **0**。11 份输入原字节核对全部通过，见 [PROVENANCE.json](PROVENANCE.json)。

## 实际改变

1. 新 `ctx-binding-v1` 扩展：宿主 verifier 认证来源，独立 authority 每次读取当前租户/issuer/actor/effectiveActor/用途登记；Platform 校验精确一致、有效期、委托决策和最小 scopes，生成独立 evidence。证据不是凭证或最终业务写许可。
2. HTTP、消息、后台 callback scope：解析前屏蔽外层，成功后仅回调内暴露；finally 撤销共享 holder 并恢复外层，隔离嵌套、并发、取消及捕获子任务；重试重新验证当前服务与登记，历史 requester 不冒执行身份。
3. 原 HTTP API 复用 scope，七字段合同和缺失/非法 snapshot 403 保留。新 HTTP API 显式 opt-in，解析失败在业务开始前映射 401/403/503；业务异常与取消继续传播。
4. 保留失败/成功证据，完整映射 28 AC 与 PGT-004。提供宿主分层采用清单、依赖、身份来源义务、旧新兼容与 PostgreSQL 后续责任。

只变更任务卡允许的 Context 源码/测试、三个现有 HTTP 文件和任务文档。原 Snapshot/IRequestContext/IRequestContextResolver、JWT/profile/cache、CloudEvent 字段/校验/原字节、EF、项目配置、依赖/锁和工作流均未改变。库不返回或接受 caller schema，不变更 TenantId/OrganizationId，不执行数据库操作。

## 验证与审查

| 本地必需项目 | 发现/通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| AspNetCoreTests Release | 243/243 | 0 | 0 |
| UnitTests Release | 259/259 | 0 | 0 |

502 条通过，其中新增行为 84 行测试。有效 RED 包括绑定/scope 未实现、HTTP 未实现及嵌套恢复；含空格 issuer 的边界失败原始记录保留，修正后完整项目已回归通过。没有跳过项或零发现作为通过。

命令、环境、TRX、日志和计数见 [VALIDATION.md](VALIDATION.md) / [test-summary.json](test-summary.json)。测试实际源码原字节、依赖图及程序集 hash 见 [full-test-inputs.json](full-test-inputs.json)，Git 规范化后的 197 个源码/项目 blob hash 见 [tested-git-blobs.json](tested-git-blobs.json)。源码/依赖不变时复用这些结果，不将提交或合并后的 SHA 写成一次新的测试执行。

独立只读集中审查结论 **Ready to merge（限本窗口）**；Critical/Important/需修改 Minor 均为 0，见 [REVIEW.md](REVIEW.md)。静态检查确认授权路径、只读合同字节、相对链接、源文件哈希、敏感材料扫描与映射测试引用，见 [static-checks.json](static-checks.json)。

## 未验收与下游责任

- [ACCEPTANCE.md](ACCEPTANCE.md) 和机器映射保留所有原 28 AC / PGT-004 的 NOT_RUN；本次只给有真实 TRX 的子断言 PASS_LIBRARY_ONLY。
- 实际 Main/CRM HTTP、sidecar、producer、consumer、worker 的认证/登记适配器及逐入口包引用/注册/启用/运行/业务记录待各宿主任务采用。本次 fixture 不是生产验证器。
- 真实 PostgreSQL 租户目录/region/issuer→schema、取得业务连接前拒绝、连接次数/池隔离/权限/无业务写集证据均 NOT_RUN。Context 不能替代同事务 Owner 最终门、S6 内容门、单 writer 或 typed replay 三动作。
- 旧新 snapshot/CloudEvent 真实消费者回归、UI 切组织不回显、P03 实际 JWT/轮换/cache/profile 运行采用未在本任务验证；其现有源码及合同保留。
- 247 整体运行场景、41 历史静态 REFERENCE_ONLY、缺失 ITER/REL 原件和 WP05 PROPOSED 不变。不因本任务交付整目标自动 DONE。
- 下游可评审本候选源码的 `ctx-v1` + `ctx-binding-v1`。**没有新 NuGet 包发布或宿主升级声明**，不能把源码候选当已发布包。

## Actions 与交接窗口

八份 candidate/protected-main 工作流字节相同：七份仅 workflow_dispatch，唯一自动 `pull_request` 为 RFC3161 preflight，仅匹配其工作流自身和专属 ReleaseTests 文件，本任务路径不匹配；没有 push/workflow_run/pull_request_target 自动入口。依赖/制品消费链未改。检查证据见 [PUSH-CHECK.json](PUSH-CHECK.json)；真正 push 前再次刷新远端并核对最终路径。

Actions 未运行/重跑、无需取消（queued/in_progress 均空）；触发配置修改 0。无包发布、数据库迁移、既有环境切换或部署。Handoff 已确认私有且 Actions disabled，候选/默认分支无工作流。

COORD 请在任务交付窗口检查最终候选固定提交及上述验证适用性，统一更新 Main roadmap、生成视图及四份状态文档，按正常保护串行合并，再核对各仓远端 main 包含性与必要集成验证后登记任务状态。执行对话未修改共享状态、未创建或合并 PR、未清理任务 worktree/分支。
