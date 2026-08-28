# P05-S01 — Dapr Service Invocation、Pub/Sub 与 Kafka 约定

| 项目 | 内容 |
| --- | --- |
| 决策状态 | Accepted implementation scope |
| 实现状态 | Candidate；等待 PR/main、不可变发布与 CRM 固定版本消费 |
| 版本 | `0.5.0.0` / package metadata `0.5.0-alpha.0` |
| 前置 | P02 RequestContext、P04 CloudEvents/JSON Schema bundle |
| 日期 | 2026-08-28 |

仓库交付版本使用四段 `VERSION`：`0.5.0.0`；本实现候选的 package metadata `0.5.0-alpha.0` 只用于可重复打包和 PR 验证，不在 S01 发布。

## 1. 目标

P05 让业务服务通过同一个 `CP6.Platform.Messaging` 包调用 Dapr service invocation，并把已通过 P04 验证的 structured CloudEvent 发布到 Dapr Kafka Pub/Sub。它固定 transport addressing，避免每个业务仓库自行发明 component、topic、partition key 或消费校验。

生产者必须在任何网络调用前验证 P04 完整 envelope。消费者必须在业务 handler、副作用或持久化前再次验证 envelope，并核对 Dapr 交付的 Kafka topic 和 record key。错误结果只返回稳定失败分类，不回显 payload 或 PII。

## 2. 冻结约定

- Dapr Pub/Sub component 名为 `cp6-kafka-pubsub`。
- Kafka topic 为 `cp6.<producer>.<event-slug>.v<major>`，由 P04 event type 唯一派生。
- Kafka partition key 为 `<tenant UUID>/<aggregateid>`；同租户同 aggregate 保持顺序，不允许调用方指定 partition number 绕过该规则。
- 发布内容类型固定为 `application/cloudevents+json`，直接发送 P04 structured envelope，不允许 Dapr 重新包装普通 JSON。
- Dapr AppId 必须是以 `cp6-` 开头的 lowercase DNS label；method name 必须是无 traversal、query、fragment 的相对路径。
- 传输成功只表示 Dapr/Kafka 接受消息；端到端 exactly-once 不成立。至少一次投递、事务原子性、重试、lease、retention 和 DLQ 由 P06 负责。

## 3. 自动化验收

`pwsh ./eng/verify.ps1 -Gate Integration -Profile p05-real` 必须启动固定版本的真实 Dapr `1.18.2` sidecars 和 Apache Kafka `4.3.1`，并证明：

1. CP6 包通过本地 sidecar 使用 service invocation 调到另一个 Dapr AppId；
2. CP6 publisher 在 P04 验证后以 canonical topic/partition key 发布 structured CloudEvent；
3. Dapr Kafka consumer 把同一事件交给 receiver，receiver 在记录副作用前通过 envelope/topic/key 三重验证；
4. 测试证据记录镜像、topic、partition key、event id 和 invocation 结果，不记录业务 payload；
5. 容器和网络只属于本次测试 project，结束时精确清理，不执行全局 Docker prune。

Windows/Linux 常规 CI 继续执行跨平台构建、单元、ASP.NET、契约与安全门禁；真实容器门禁在独立 Ubuntu job 运行，发布工作流也必须再次运行同一 `p05-real` profile。

## 4. 明确不做

- 不实现 EF Outbox/Inbox、lease、retention、DLQ、重放 worker 或 SQL migration；这些属于 P06。
- 不提交 DEV/UAT/PROD Dapr component、Subscription、Topic/ACL provisioning、NetworkPolicy、Secret 或部署资产；这些属于 P09。
- 不创建 CRM 业务事件、`CP6.CRM.Worker` 或运行时订阅；需要独立的 CRM-F3-CONTRACT/C02/P06 业务切片。
- 测试 Kafka 使用隔离 Docker network 上的 `authType: none` 和 `disableTls: true`，不得复制为非生产或生产配置。

## 5. 完成定义

P05 只有在以下全部完成后才可从 `Candidate` 改为 `Frozen / Consumable`：Platform PR/main 双平台与真实 Dapr/Kafka 门禁通过；从精确 main 发布不可变 `0.5.0-alpha.1` 并保存逐包 SHA-256；CRM 固定版本恢复、使用同一约定做无副作用消费验证；Platform、CRM、公共 CP6 三仓 locator/项目记忆全部绑定精确提交和运行证据。

当前 S01 不发布包、不启用 CRM runtime、不创建云资源或部署。
