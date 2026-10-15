# WP02-12 显式传输 V1

本实现提供 opt-in 通用消息接线：Host 固定路由、生产者资格、精确合同选择、原字节发布和持久隔离读回。旧 P04/P05 API 及既有业务路由保持原语义。C02 新采用关闭；不能把通用斜杠 key 用于原 C02 冒号 key。需要真实 WP02-05/06/29、原生 producer/consumer manifest 和独立窗口才能采用 C02。

## 使用与信任边界

1. 由可信启动配置调用 `Cp6ContractBundle.LoadForTransport(directory, version, manifestSha256)`。每个精确版本使用独立 bundle/validator；schema 引用限定为本地文档 fragment。不远程取 schema、不热更新、不尝试 latest。
2. 创建不可变 `TransportRouteV1`，登记准确 profile、route、type、schemaId、schemaVersion、schemaHash、producer、source、topic。`TransportRegistryV1` 是 Host 静态接线表。同五元组重复即启动失败，即使 hash 相同；同 Host 路由的权限/地址策略必须一致。可信 endpoint/subscription 选择 profile/route，消息和 HTTP header 无选择权。
3. `ITransportAuthorityV1` 是可信 Host 实现边界，必须验证真实 channel、producer entitlement、tenant/region、实际 topic/key、原字节 hash 及已有外层消息身份。其 DTO 不是密码学证明；不能把请求字段装进 facts 就称认证通过。发送资格必须来自已提交的 Owner send-intent。实际 Kafka 没有生成 aggregateversion 等业务字段；必须分别标明 Owner 外层身份与 envelope 待验证字段，不能称这些字段由 broker 认证生成。
4. `QualifyAsync` 先按 `min(profile, host, broker)` 拒绝越界，再拥有原字节副本、认证固定路由、严格解析、精确选择、schema/封装与 metadata 校验。通用上限 1MiB/depth64。重复业务成员拒绝；既有根 trace 重复/非法丢弃 trace 的兼容规则保留，原体不改。安全结果只含固定类别，无 payload 或用户字段路径。
5. 仅 `IsQualified` 成功的 Publish capability 能交 `PublishAsync`。异常、取消、timeout、未定义响应均为 Unknown，保原 message/mutation，不自动切 publisher 或生成新 ID。Confirmed 仅表示运输接受。
6. Receive 的未通过合同结果可能带有 **仅供隔离** 的 capability。不能因 `Message != null` 就进入业务。`QuarantineAsync` 调用可信 Host store 提交原体，再从独立 committed transaction 读回 profile/route/policy/id/hash/nativeReference/全部 native facts/原字节。全部相等才 Drop，否则 Retry。同一原体重投到新 offset 时保留不同 nativeReference 的观察，不能覆盖首次隔离，也不能把新 offset 当新业务消息。已认证的错误 key 仅可保留实际 key 后隔离。内存 store 或伪造 readback 不能证明持久性。未知合同不改写 schema 版本。

`VerifiedTransportMessageV1` 构造器不公开，绑定创建它的 registry 实例和 purpose；对外只返回字节副本。raw-envelope SHA、schema SHA、Owner 输入摘要及 S6 native digest 始终分域。本库不创建另一套业务幂等账、不定义业务 key，也不替 Owner 做权限/Inbox/业务同事务。

## Outbox 与 ACK

`EntityFramework.TransportV1.TransportOutboxPublisherV1` 仅提供 `IOutboxPublisherV2` 的有界 delegate bridge。EF 不引用 Messaging；Host 同时引用两者并组合。使用已修 #66/#67 的 OutboxV2 dispatcher，保短事务提交 send-intent、事务外网络、剩余发送预算和原 mutation 查询。bridge 本身不授予 Owner 权限，不使用旧 dispatcher fallback。

接收端只有真实 Owner 的 Inbox/业务/原回执同事务提交后才可声称业务 SUCCESS。本任务 fixture 只提交受控的运输观察，没有 Main/CRM 业务处理器；它的 SUCCESS 明确表示 fixture observation commit。真实业务路由、跨消息同 business key 去重、原 S6 优先序和正式 release trust 均未由此取得资格。

## 本地原生验证

入口在 `tests/wp02-12-runtime/`。所有阶段要求完整 owner 和该 owner 的私有 settings；使用本任务 PostgreSQL55430、Kafka4.3.1、Dapr1.18.2 和固定缓存 image digest。没有拉镜像，没有 Actions 或生产环境操作。SDK8 本地离线恢复；该验证关闭 NuGet 在线 audit，未声明漏洞扫描通过。

Kafka 使用独立 publisher/receiver/observer/intruder SASL 身份和 topic ACL；未授权 producer/topic 的真实发送被拒绝。Dapr API token 与 app token 分开；接收端核实际 Kafka record 的 topic/key/partition/offset/body bytes。独立 observer 使用 Kafka console consumer 的字节输出，topic 来自受控订阅参数，key/offset 来自 broker record，不能用 payload 重算充证据。受控 fixture delimiter 不允许出现在测试原体；此观察器不是任意生产流量解析器。

宿主暴露仅 loopback；运行使用任务专属 Docker 网络，host/Dapr channel 的本地 token 认证是本机资格化边界。SASL/PLAIN 不声明生产 TLS/mTLS/网络隔离。Kafka admin 和本机 owner 为本夹具可信部署权限；不能把任意能修改 ACL/私有 observation 文件的本机进程视作不可信但仍宣称它无权。

PG setup/admin 与最小 DML runtime 分开；每次连接核 owner、marker、endpoint、system identifier、database OID/name/owner、session_user。测试专属表、OutboxV2 guard 和原有模型只装入独占 fixture schema。原体和 metadata 进入受限隔离库，公开错误不包含它们。读取恢复只读，不编辑 payload，不提交业务或自动重放。

证据和未覆盖项见 [任务报告](development/evidence/WP02-12/REPORT.md)、[验收映射](development/evidence/WP02-12/acceptance-map.json)、[库测试](development/evidence/WP02-12/tests/library/RUNS.md)。原40项业务/Owner AC 保持独立 NOT_RUN；本地子断言以实际运行结果逐项列出。
