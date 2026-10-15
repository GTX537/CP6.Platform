# WP02-12 交付报告 — C / 第七轮

交付范围是 opt-in 有界传输库和独占本地 Kafka/Dapr/PostgreSQL 验证。候选交付后由 COORD 集中审查、串行合并、核远端 main 并同步台账；本报告不将候选记为主线完成。C02 新采用关闭，真实 Main/CRM Owner、生产 SLO、发布和业务验收未通过本任务取得资格。

## 来源与实现

- Main 执行包：`99fc0ce383d2d02c1404a8adbb3abe971b41d332`；Handoff 分配：`ebeae808b2aa35c8c9115519fed564edebbbde0c`。
- Platform 创建基线及收尾核对的远端 main：`b28db763f70ab6f164cb7c87d6ce49a6d97267f1`。任务分支：`codex/wp02-12-message-transport-20261014`；最终候选 SHA 由固定 Handoff DELIVERY 引用，避免提交自引用。
- START：`cd3e3ca14a1cf31bd6efbf62b32e2cd6602b2f8d`，消息原字节 SHA256 `ab5230d1f399784f5fc901c942567ed9ed241766a246884dfc3c2c3474247ac8`，2039 bytes。
- 只修改获准的 TransportV1、两个旧文件的显式版本入口、任务专属测试/fixture/runtime 和文档证据。生产项目/根依赖、合同权威、OutboxV2/InboxV1、生产迁移及四份状态文件保持只读。

新增精确静态五元组路由、独立固定版本 bundle、重复注册拒绝、可信 Host 认证接口和实例绑定 capability；原字节有界持有、固定安全错误码、原根 trace 兼容。发送异常/取消/不确定结果保持 Unknown。EF 仅有委托桥接，使用已修正的 OutboxV2，不引入反向 Messaging 依赖。隔离 DROP 需要持久提交后独立读回全部原字节、hash 和 native facts；隔离身份含 nativeReference，保留重复原消息的不同运输观察。

接口与采用边界见 [使用说明](../../../WP02-12-MESSAGE-TRANSPORT-V1.md)。`ITransportAuthorityV1` 和隔离 store 是可信 Host 边界；实现接口或传入 facts DTO 本身不是认证/持久性证明。

## 实际验证与复用

| 验证 | 实际结果与来源 | 适用范围 |
|---|---|---|
| 库完整用例 | 44/44；`tests/library/20261015T003737829Z/green-native-quarantine.trx` | 修复前完整库快照 |
| 审查修复定向回归 | 27/27；`tests/library/20261015T004920812Z/green-native-reference-key-quarantine.trx` | nativeReference、错误 key 隔离及关联身份校验；含 3 个新增用例，与前行重叠，不能相加为独立总数 |
| 受影响旧合同 | 101/101；`tests/library/20261015T003413218Z/legacy-transport-regression.trx` | 旧 API、Dapr/Kafka、native profile、trace；后续修改仅在 TransportV1，复用原结果，不称重跑 |
| 架构 | 98/98；`tests/architecture/wp02-12-architecture.trx` | 一次任务收尾架构验证 |
| 真实运行初次完整集 | 22/22；[原结果](runtime/acceptance.json) | 原生 Kafka/Dapr/PG，实际认证/ACL、独立 record、Outbox、原体隔离、受控服务不可用；fixture Owner |
| 审查修复真实定向集 | 7/7；[原结果](runtime/targeted-review-regression.json) | 同一非法原体 offsets 5/7 各自隔离并 DROP；错误实际 key 持久隔离 |

源输入、依赖与产物身份：[validation-inputs.json](validation-inputs.json)、[validation-artifacts.json](validation-artifacts.json)；所有 TRX 索引：[test-runs.json](test-runs.json)。具体命令、RED、编译失败和复用说明：[RUNS.md](tests/library/RUNS.md)。最终 fixture DLL SHA256 为 `075b9360818b5279e892602bb633e72df91d2a2e285999a344c8c54131629820`。工作字节与测试输出按 hash 绑定；提交 SHA 改变未声称重新构建。SDK8 本地离线缓存恢复，未放宽包信任；在线 audit 关闭，不声明漏洞扫描。

真实集原结果完整保留；初次 wrong-key RETRY 被定向持久隔离结果替代。错误 key 案例导出的证据证明隔离原体/实际 key，不声称该案例单独导出了 DROP observation。两个未知 schema offset 则都有真实 Dapr DROP 观察。[复用边界](runtime/reuse-boundaries.json)明确原结果适用范围。

## 原生证据和资源收尾

本任务 owner：`6caf6a19b8a641989bfe8e108733d5bc`。独占 PostgreSQL `127.0.0.1:55430`，数据库 `cp6_wp0212_20261015_6caf6a19`、OID `16385`、system identifier `7696683980637488880`。专属 Kafka4.3.1 与 Dapr1.18.2 使用已缓存固定 image ID，没有拉镜像。

Kafka 独立 SASL publisher/receiver/observer/intruder 和 topic ACL；真实错误 producer/topic 发送被拒绝。Dapr API/app token 分离，伪造直接请求被拒绝。独立 Kafka console consumer 读取原 key/partition/offset/body；topic 取自受控原生订阅，非 payload 重算。Host 权限来自专属 topic writer ACL、认证 sidecar 和独立 record 匹配；部署 admin、本机私有观测文件属于可信边界。SASL/PLAIN 与本地 token 不证明生产 TLS/mTLS。Dapr Kafka 的配置语义参考 [官方组件说明](https://docs.dapr.io/reference/components-reference/supported-pubsub/setup-apache-kafka/)。

实际 ACK 丢失案例为应用在真实 Dapr 接受后抑制成功结果的明确故障注入；证明原 Outbox mutation 保持 Dispatching 且 broker 已有原字节，不冒充 provider 原生 ACK 丢包。接收 SUCCESS 仅证明 fixture ReceiveObservation 提交，没有真实业务副作用或 Inbox 业务同事务声明。

[原生命令](runtime/commands.json)保留非敏感 stdout/stderr 原字节 Base64；container inspect 含凭据，只发布 hash，原日志留在私有目录。[私有日志身份](runtime/private-log-identities.json)、[最终 record](runtime/final-observed-records.json)、[最终 ACL](runtime/final-acl-proof.txt)、[停止前身份](runtime/native-identity-before-stop.json)、[停止结果](runtime/stop-result.json)可交叉核对。最终 5 个自有容器均停止，fixture 已退出，PostgreSQL smart-stop；19092/13500/15001/18012/18013/55430 已关闭。所有数据、容器、网络和失败日志保留，无删除/prune。初始 internal 网络被保留；实际增加任务自有 host bridge 以支持 Docker Desktop loopback 映射，最终绑定/网络身份以停止前原生检查为准。

## 失败、审查与未验证项

[审查记录](REVIEW.md)包含一次完整任务审查、P2 问题及定向关闭证据；[阶段失败](phase-failures.json)保留启动/观察器/停止保护检查失败的真实原因。没有将不完整运行改写为通过。凭据和私有 settings 从未纳入公共证据，导出前逐值扫描生成的 secret。

[原40项 AC 映射](acceptance-map.json)逐项挂本地子断言，原业务 PASS 为 0，全部保持 NOT_RUN。真实 Owner 权限/事件清单、跨消息 business key 幂等、Main/CRM 同事务业务、C02 colon key 与 WP02-05/06/29 采用、S6 原优先序、生产 retention/recovery/SLO、版本发布信任和生产部署均未验证。测试生成 bundle 仅是测试资产，不是新发布合同。

本轮未触发、重跑或取消 Actions，未修改工作流/保护规则、推 tag、发布包或部署。每次 push 前候选与受保护 main 的工作流、依赖和完整文件范围重新核对，结果见 `prepublication-workflows.json`；Handoff START 固定原字节复核见 `start-transport.json`。总台账/生成页/四份状态由 COORD 在合并窗口统一同步。
