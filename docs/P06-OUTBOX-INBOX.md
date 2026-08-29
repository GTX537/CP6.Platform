# P06-S01 — EF Outbox/Inbox、Lease、Retention 与 DLQ

| 项目 | 内容 |
| --- | --- |
| 决策状态 | Accepted implementation scope |
| 实现状态 | Candidate |
| 版本 | `0.6.0.0` / package metadata `0.6.0-alpha.0` |
| 前置 | P02 RequestContext、P04 CloudEvents/JSON Schema、P05 Dapr/Kafka addressing |
| 日期 | 2026-08-29 |

仓库交付版本使用四段 `VERSION`：`0.6.0.0`；候选 package metadata `0.6.0-alpha.0` 在 Platform main、不可变发布和 CRM 固定版本 SQL 消费闭环完成前不得标记为 `Frozen / Consumable`。

## 1. 目标与事务边界

P06 在 `CP6.Platform.EntityFramework` 中提供可嵌入消费方 `DbContext` 的 Outbox、Inbox、aggregate checkpoint 与 dead-letter 模型。Platform 不拥有业务数据库，也不替消费方生成 migration；消费方必须在 `OnModelCreating` 调用 `AddCp6TransactionalMessaging()`，并把这些表纳入自身迁移。

生产者先完成 P04/P05 envelope 校验，再通过 `Cp6OutboxStore<TContext>.Enqueue` 把消息加入调用方现有 `DbContext`。`Enqueue` 故意不调用 `SaveChanges`：业务变更与 Outbox 必须由调用方在同一数据库事务中保存；不能跨数据库或远程调用声称原子性。

消费者先完成 envelope/topic/partition 校验，再由 `Cp6InboxProcessor<TContext>` 开启 `Serializable` SQL 事务。handler 只能写入传入的同一个 `DbContext`；业务变更、aggregate checkpoint、Inbox 完成状态及 handler 产生的结果 Outbox 一起提交。handler 内不得执行邮件、HTTP、broker publish 等不可回滚远程副作用。

## 2. 冻结语义

- Outbox `MessageId` 唯一；payload 保存 SHA-256，最大 1 MiB。发布 claim 使用条件更新和唯一 lease token，过期 worker 不能确认新 owner 的 lease。
- broker 接受后、Outbox 标记 Published 前崩溃会在 lease 过期后重投。因此保证是 at-least-once：允许重复，不允许以 exactly-once 名义隐藏丢失窗口。
- publisher 报告可重试失败时使用有上限的指数退避；达到 `MaxOutboxAttempts` 或不可重试失败时，Outbox 与内容安全的 DLQ 记录在同一 SQL 事务中更新。
- Inbox 数据库唯一键为 `(ConsumerName, MessageId)`。相同 id/相同 payload hash 返回 Duplicate 且不运行 handler；相同 id/不同 hash 返回 `CP6_INBOX_PAYLOAD_CONFLICT` 并写入 DLQ。
- `(ConsumerName, TenantId, AggregateId)` checkpoint 只单调递增。小于或等于已处理版本的新消息记为 `CP6_INBOX_OUT_OF_ORDER`，提交 Inbox 结果但不运行 handler。
- handler 失败时当前业务事务完全回滚，失败次数在独立事务持久化。达到 `MaxInboxAttempts` 后 Inbox 标记 dead-lettered；DLQ 只保存 hash、稳定错误码和 support reference，不保存原始 payload。
- Outbox 与 Inbox 授权重放由调用方先完成身份/权限检查，再调用 requeue API。API 记录稳定 reason code 并保留原始 `MessageId`；入站原始 payload 的受保护来源仍是 broker/归档，不由 DLQ 表复制。
- 默认保留期：成功 Outbox 7 天、成功/忽略的 Inbox 30 天、DLQ 与 dead-lettered Outbox/Inbox 90 天。Pending/Dispatching Outbox 不参与清理，dead-lettered Inbox 在可重放审计窗口结束前不会提前删除。

## 3. 接入示例

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddCp6TransactionalMessaging();
}

await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
db.Orders.Add(order);
outbox.Enqueue(validatedEnvelope); // 不单独 SaveChanges
await db.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

消费 handler 使用 processor 提供的同一 context 创建业务写入和结果 Outbox；processor 负责最终 `SaveChanges`/commit。应用必须为每个 dispatcher 实例使用唯一 worker id，并把 `Cp6OutboxPublishException` / `Cp6InboxProcessingException` 中的稳定错误码映射到可观测性系统，禁止把 payload 或 PII 放进 error code/support reference。

## 4. 自动化验收

`pwsh ./eng/verify.ps1 -Gate Integration -Profile p06-real` 启动不可变 digest 固定的 SQL Server 2022 CU26，并证明：

1. 业务变更与 Outbox 同事务 commit/rollback；
2. 两个 worker 竞争时只有一个 claim，过期 lease 可恢复，旧 lease token 无法确认；
3. broker ack 后进程崩溃窗口会重投同一 message id，不丢消息；
4. Inbox duplicate、payload-hash conflict 与 aggregate version 乱序均在 handler 前阻断；
5. handler 即使先 `SaveChanges` 再失败，业务写入和结果 Outbox 仍回滚；bounded retry 进入 DLQ 后可审计重放；
6. 7/30/90 天 retention 只删除已终结数据，不删除 pending Outbox；
7. 夹具创建随机隔离数据库并在结束时删除，脚本只精确清理本次容器，不执行全局 Docker prune。

Windows/Linux 常规 CI 继续运行跨平台门禁；真实 SQL Server 在独立 Ubuntu job 运行。发布 workflow 必须同时重跑 P05 Dapr/Kafka 与 P06 SQL Server profiles。

## 5. 明确不做

- 不创建 CRM Worker、业务事件、subscription 或 runtime topic；首个授权异步业务切片另行完成。
- 不提交 CRM/其他服务的 migration；Platform 提供模型，消费方拥有数据库与迁移历史。
- 不提交 DEV/UAT/PROD Dapr component、Kafka ACL/topic、secret、NetworkPolicy 或部署资产；这些仍属于 P09。
- 不在 P06 实现 P08 tracing/resiliency 阈值，也不承诺 broker/数据库分布式 exactly-once。

## 6. 完成定义

P06 只有在 Platform PR/main 的 Windows、Linux、真实 Dapr/Kafka 与真实 SQL Server 门禁全部通过，从精确 main 发布五个不可变 `0.6.0-alpha.1` 包并保存逐包 SHA-256，再由 CRM 固定版本恢复并用真实 SQL Server 证明重复/冲突/乱序均无业务副作用后，才可改为 `Frozen / Consumable`。Platform、CRM 和公共 CP6 项目记忆必须绑定精确 commit、run、artifact 与 package digest。
