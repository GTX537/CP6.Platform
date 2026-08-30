# P08 Observability、Health、Resilience 与 SLO Evidence 设计

| 项目 | 值 |
| --- | --- |
| 里程碑 | `P08` |
| 设计状态 | Approved design / implementation not started |
| 日期 | 2026-08-30 |
| 权威前置 | P03、P05、P06 均为 `Frozen / Consumable`；P07 已完成但不是 P08 前置 |
| 候选版本 | repository `0.8.0.0` / package `0.8.0-alpha.1` |
| 完成证据 | 跨两个独立服务的 Trace、故障注入、不可变包发布与 CRM 固定版本消费 |

## 1. 背景与目标

公开执行规格把 P08 定义为 OTel、健康、resiliency 与 Runbook，前置为 P03/P05/P06，完成证据为跨服务 Trace 与故障注入。当前 Platform 已冻结 P03 correlation、P04 CloudEvents、P05 Dapr/Kafka 和 P06 Outbox/Inbox，但没有统一的遥测资源、健康/发布身份、低基数标签、HTTP resilience 或 SLO evidence schema。

P08 的目标是提供 exporter-neutral 的可复用 SDK 合同，并用确定性测试证明：

1. HTTP 与消息链路能传播合法的 W3C Trace 上下文，同时保留既有 correlation/causation 语义；
2. live/startup/ready/release 明确分离，响应不泄露基础设施或业务敏感信息；
3. timeout、circuit breaker 和显式幂等 retry 具有失败关闭边界；
4. Outbox/Inbox、Dapr/Kafka 与 HTTP 具有稳定 Trace/Metric，而 instrumentation 不改变既有事务和副作用语义；
5. SLO 证据可被版本化、内容寻址、判定完整性并绑定不可变发布身份；
6. Platform 从精确 `main` 发布不可变 `0.8.0-alpha.1` 后，由 CRM 固定版本完成消费验证。

## 2. 明确不做

P08 不：

- 部署 OpenTelemetry Collector、Prometheus、Grafana、Tempo 或任何 SaaS exporter；
- 提交 Compose/Kubernetes 组件、Topic/ACL、订阅、NetworkPolicy、Secret 或环境 route；
- 创建或运行 CRM Worker、业务事件订阅或真实 CRM Gateway route；
- 绑定 OTLP endpoint、认证方式、采样后端或生产 dashboard；
- 宣称 CRM 达到生产 SLO、可用性或容量目标；
- 实现 P09 环境 provision、P10 正式候选签名/System Manifest，或 CRM12 生产故障演练。

## 3. 方案选择

采用可组合 SDK 集成：Platform 使用官方 OpenTelemetry 与 .NET HTTP resilience 库，冻结 CP6 profile、传播、安全标签、健康、发布身份和测试合同；宿主选择 exporter 和环境配置。

拒绝两个替代方案：

- 纯合同层会迫使消费方重复 OTel/health/resilience 接线，无法形成足够强的跨服务证据；
- 在 P08 内交付完整观测栈会越过 P09/CRM12 的环境、运维和部署授权。

实现阶段以 Central Package Management 精确固定：

- `OpenTelemetry.Extensions.Hosting` `1.18.0`
- `OpenTelemetry.Instrumentation.AspNetCore` `1.18.0`
- `OpenTelemetry.Instrumentation.Http` `1.18.0`
- `Microsoft.Extensions.Http.Resilience` `10.9.0`

所有包仍以 .NET 8 为目标，并必须通过 restore、跨平台构建、依赖安全和包合同门禁。若实现时发现目标框架或依赖安全不兼容，只能前向更新本设计并重新评审，不能静默换库。

## 4. 包边界

P08 不增加第六个生产包。

### 4.1 CP6.Platform.Contracts

- `Cp6ReleaseIdentity`：service、version、Git SHA、artifact digest、contract bundle digest 和候选状态；
- `Cp6SloEvidenceDocument` 及 `cp6.slo-evidence.schema.json`；
- SLI 定义、UTC evidence window、measurement、source digest、completeness 和 result 枚举；
- 稳定 evidence schema ID/version。

Contracts 不引用 OpenTelemetry 或 ASP.NET Core。

### 4.2 CP6.Platform.Abstractions

- `Cp6TelemetryConventions`：ActivitySource/Meter 名称、稳定 operation/outcome/error 标签；
- health tag 常量 `live`、`startup`、`ready`；
- exporter-neutral 的发布身份读取抽象。

Abstractions 不提供 exporter、网络客户端或全局 mutable context。

### 4.3 CP6.Platform.AspNetCore

- `AddCp6Observability(Cp6ObservabilityProfile)`：注册 Resource、ASP.NET Core/HttpClient instrumentation 与 CP6 source/meter；
- `MapCp6OperationalEndpoints(Cp6OperationalEndpointProfile)`：映射 `/health/live`、`/health/startup`、`/health/ready`、`/health/release`；
- `AddCp6HttpResilience(Cp6HttpResilienceProfile)`：为命名 `HttpClient` 建立 timeout/circuit/retry pipeline；
- 安全 health writer、`Cache-Control: no-store` 与发布身份校验。

注册方法可重复调用但不得产生重复 provider、重复 endpoint 或重复 handler。配置漂移必须在启动时失败。

### 4.4 CP6.Platform.Messaging

- 在 P04 CloudEvent 上注入/提取可选 `traceparent` 与 `tracestate`；
- Dapr invocation、Pub/Sub 和 Kafka send/consume Activity；
- 发布、消费、拒绝和传播失败的低基数 Metric；
- 保留 P04 七个必需扩展和 P05 topic/key 验证顺序。

P08 不传播 `baggage`，不改变 event type、schema、topic、partition key 或业务 payload。

### 4.5 CP6.Platform.EntityFramework

- Outbox enqueue/claim/publish/retry/dead-letter Activity 与 Metric；
- Inbox validate/duplicate/conflict/process/retry/dead-letter Activity 与 Metric；
- oldest available age、attempt、lease outcome 和 bounded disposition。

Instrumentation 必须在现有事务边界外观察或在同一调用栈记录，不增加数据库写、不改变 lease、幂等、回滚、retention 或 DLQ 语义。

### 4.6 CP6.Platform.Testing

- 线程安全的内存 Activity/Metric recorder；
- 可编排的 HTTP dependency fault handler；
- Trace 拓扑、允许标签、敏感字段和 SLO evidence 断言；
- 仅在 `Test`/`CI` environment 可注册的故障注入扩展。

Testing 包不得成为五个生产包的运行时依赖。非 Test/CI 环境尝试注册故障注入必须启动失败。

Testing 继续是仓库内测试支持项目，不进入 `0.8.0-alpha.1` 发布集合。CRM 固定版本消费只引用五个生产包，并用 CRM 自有黑盒测试 host/handler 验证公开行为；不得通过跨仓 ProjectReference 或复制 Testing 源码建立消费证据。

## 5. Trace 与 correlation 数据流

### 5.1 HTTP

ASP.NET Core instrumentation 创建或接受入口 Activity；HttpClient instrumentation 传播 W3C 上下文。P03 的 `X-Correlation-Id` 继续作为安全的支持/错误关联标识，并写入 `HttpContext.TraceIdentifier`。Correlation 与 Trace ID 互不派生、互不替代。

P08 的出站 correlation handler 只从当前已验证的 `HttpContext.TraceIdentifier` 或 `IRequestContext` 复制一个安全值；它先删除调用方预置的冲突/重复 `X-Correlation-Id`。后台操作没有已验证 correlation 时生成新的安全值，不从 Trace ID、用户输入或 identity header 推导。

入口顺序为 correlation → authentication/request context → endpoint。Trace 上下文不能提供 User、Tenant、Organization、Function、DataScope 或后端认证权威。

### 5.2 Messaging

Producer 使用当前 Activity 注入可选 `traceparent`/`tracestate` CloudEvent extensions。Consumer 先解析遥测上下文，再运行 P04 schema/region 和 P05 topic/key 门禁；只有全部业务合同通过才进入 P06 Inbox/handler。

`correlationid` 继续表示跨请求支持链，`causationid` 继续表示业务因果，W3C parent 表示诊断拓扑。三者必须分别保存。

### 5.3 非法远端上下文

非法、超长或重复的 `traceparent`/`tracestate` 被丢弃，处理器创建新 root Activity，并增加稳定的 propagation-rejected 计数。原值不进入 log、tag、Problem Details 或 health response。

遥测上下文是可选诊断数据。它无效时不能绕过 P04/P05/P06 门禁，也不能单独改变业务成功/失败；合法业务消息仍可在新 Trace 中处理。

## 6. 标签与数据安全

默认 Resource/Trace/Metric 只允许：

- `service.name`、`service.version`、`deployment.environment.name`；
- CP6 region、稳定 operation、outcome、error code；
- 标准 HTTP/messaging/database 协议类别；
- release Git SHA/artifact digest 仅作为 Resource 或 release evidence，不作为高频 Metric dimension。

Platform 默认禁止以下内容成为 Metric label 或自动 Span attribute：

- User/Organization 名称或 ID、Email、Phone、Address；
- 资源 ID、完整 URL/query、原始 route value；
- Token、Cookie、connection string、Topic ACL、Host；
- request/response body、CloudEvent data、异常自由文本；
- correlation ID、event ID、trace ID 等高基数值。

稳定错误码可记录；异常类型仅在固定 allowlist 内记录。Exporter 失败、队列饱和或导出超时必须被 OTel SDK 有界处理，不能阻塞或改变业务响应。

## 7. Health 与发布身份

### 7.1 端点语义

| 端点 | 语义 | 状态规则 |
| --- | --- | --- |
| `/health/live` | 仅进程/事件循环存活 | 不运行外部依赖；存活为 200 |
| `/health/startup` | 配置与必要初始载入完成 | 任一非 Healthy 为 503 |
| `/health/ready` | 消费方登记的当前必要依赖 | 任一非 Healthy 为 503 |
| `/health/release` | 不可变发布身份 | 完整且匹配为 200，否则 503 |

消费方通过 health tags 登记 startup/ready 检查。Platform 不读取连接串、不自动探测任意 Host，也不决定哪些业务依赖是必要依赖。

响应只包含 schema version、整体状态、稳定组件名/状态、UTC observedAt 和安全 release identity。禁止输出 exception message、duration detail、Host、database、topic、tenant、Secret 或任意 health data dictionary。全部端点返回 `Cache-Control: no-store`。

### 7.2 发布身份模式

- `Candidate`：要求 SemVer、40 位 Git SHA、`sha256:` artifact digest 和 contract bundle digest；缺失或格式错误启动失败；
- `NonCandidate`：允许本地开发/测试的明确标识，但响应必须显示 `candidate=false`，且不能生成 Pass 的 SLO evidence。

环境变量或配置只能提供值，不能降低校验。P10 将负责签名和 System Manifest 对账。

## 8. HTTP resilience

每个命名客户端必须显式选择 operation kind：

- `IdempotentRead`：GET/HEAD/OPTIONS，可按 profile 对批准的 transient 状态/异常重试；
- `IdempotentWrite`：只有请求携带格式有效且稳定的 `Idempotency-Key` 时可重试；
- `NonIdempotent`：禁止 retry；
- 未分类：启动或客户端注册失败，不使用宽松默认值。

全部 profile 可配置 attempt timeout、total timeout 和 circuit breaker。禁止 hedging、自动 fallback、无限 retry 和开放式异常 predicate。Retry 次数、timeout 与 breaker 参数有上下界，并在注册时验证。

取消必须立即传播。Circuit open 或 timeout 返回稳定失败类别供消费方映射，但 Platform 不替业务服务生成成功响应，也不把未知写结果自动重放。

## 9. SLO evidence schema

`cp6.slo-evidence.schema.json` Draft 2020-12 至少包含：

- `schemaVersion`、`evidenceId`、`generatedAtUtc`；
- `release`：service/version/Git SHA/artifact digest/contract bundle digest/candidate；
- `sli`：稳定 ID、definitionVersion、unit、aggregation、comparator、threshold；
- `window`：UTC start/end、expected/observed coverage；
- `measurement`：sampleCount、numerator/denominator 或 percentile/value、excludedCount；
- `sources`：source type、稳定 source ID、query/dashboard definition digest、evidence artifact digest；
- `completeness`：`Complete`、`Partial`、`Missing`；
- `result`：`Pass`、`Fail`、`Indeterminate`。

只有 Candidate release、完整 UTC window、`Complete`、有效 source/artifact digest 和满足阈值的 measurement 才能为 `Pass`。缺样本、部分窗口、定义漂移、release 不匹配或无法验证的排除项必须为 `Indeterminate` 或 `Fail`，不能默认通过。

P08 的示例仅使用合成测试数据并明确 `productionSloClaimed=false`。

## 10. Runbook 合同

P08 提供模板和测试环境示例，至少覆盖：

- Trace 传播中断或 exporter 不可用；
- startup/readiness 失败；
- circuit open、timeout 或 dependency 恢复；
- Outbox oldest age、consumer lag、retry/DLQ 增长；
- 发布身份或 SLO evidence digest 漂移。

每个 Runbook 包含症状、影响、稳定 dashboard/query ID、安全诊断、containment、恢复、验证、升级和证据保留。示例不包含生产地址、Secret、真实 Owner、真实告警通道或部署命令。

## 11. 自动化验收

### 11.1 Unit 与 contract

- profile、release identity、operation kind 和上下界验证；
- 允许/禁止标签与敏感数据扫描；
- SLO schema 正反向 fixture、重复 JSON property 和 digest 漂移；
- 非 Candidate 或不完整 evidence 不得 Pass；
- instrumentation 不改变 P04/P05/P06 的公开合同。

### 11.2 ASP.NET integration

- live/startup/ready/release 的 tag 过滤、状态码、`no-store` 和安全 body；
- 重复注册、配置漂移和缺失 Candidate identity 启动失败；
- exporter 未配置时业务请求仍成功；故障 exporter 不泄漏或改变响应。

### 11.3 跨服务 E2E

测试进程启动两个独立 Kestrel host：Service A 经命名 HttpClient 调用 Service B，并断言同一 Trace 的 server/client/server 拓扑、不同 Span ID、release Resource、correlation 独立传播和允许标签。

负向覆盖非法/重复/超长 Trace 上下文、外部伪造 identity tags、B 不可用、取消与 exporter failure。

### 11.4 Resilience 故障注入

- 幂等读前两次 transient 失败后成功，attempt 数精确；
- 幂等写有稳定 key 才可重试；缺 key 在网络前失败；
- 非幂等/未知操作只执行一次；
- total/attempt timeout、circuit open/half-open/recovery、取消均为确定性结果；
- Test/CI 以外环境拒绝 fault injector。

### 11.5 Messaging 与真实回归

- CloudEvent trace 注入/提取与非法上下文新 root；
- P04 schema/region 和 P05 topic/key 仍在副作用前失败；
- P06 handler rollback、duplicate/conflict/order、retry/DLQ 语义不变；
- 真实 Dapr/Kafka 与真实 SQL Server profile 全部通过。

## 12. 交付顺序与完成定义

1. `P08-S00`：本设计经批准并提交；
2. `P08-S01`：Platform 实现、测试和文档，PR/main 的 Windows、Linux、真实 Dapr/Kafka、真实 SQL Server 门禁通过；
3. `P08-S02`：从精确 Platform `main` 发布五个不可变 `0.8.0-alpha.1` 包和 hash artifact；
4. `P08-S03`：CRM 只固定引用五个生产包，并用自有黑盒测试 host/handler 证明跨服务 Trace、health redaction、release identity、resilience 与故障注入；不引用 Platform Testing、不注册真实 exporter/策略；
5. `P08-S04`：CRM locator 冻结为 `Frozen / Consumable`；
6. `P08-S05`：公共 CP6 项目记忆绑定 producer/package/consumer PR/main/run；
7. `P08-S06`：Platform 最终证据文档冻结并通过 PR/main 门禁。

只有七步全部完成，P08 才可标记为 `Frozen / Consumable`。任何单仓测试、候选实现、包已上传或示例 Trace 都不能提前关闭 P08。

## 13. 安全与授权边界

本设计不授权真实 exporter、Collector、dashboard、alert、Worker、subscription、Gateway/auth runtime、C01/C02/CRM03、P09 provision、P10 候选或部署。后续任务必须继续使用独立分支、固定版本、负向测试、不可变证据和消费方验证。
