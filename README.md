# CP6.Platform

CP6 分布式服务的共享契约与基础设施包。本仓库是 Platform 包的唯一生产源；业务服务只通过版本化 NuGet 包消费，不复制实现源码。

本地需要安装 .NET 8 SDK；`global.json` 允许在 .NET 8 内滚动到已安装的最新 feature band，但不会静默改用更高主版本。

P01 已建立可验证的仓库和包边界；P02 已交付只读 RequestContext。P03 进一步交付严格 RS256/JWKS 验证、RFC 9457 Problem Details 和 correlation 边界；不可变 `0.3.0-alpha.1` 已发布并通过 CRM PR/main 固定版本消费验证，状态为 `Frozen / Consumable`。P04 增加 CloudEvents 1.0 structured JSON、Draft 2020-12 验证和内容寻址 contract bundle；四个不可变 `0.4.0-alpha.1` 包已由 exact-main workflow 发布并通过 CRM 消费验证。P05 的 Dapr service invocation、Pub/Sub 与 Kafka topic/partition 约定已由真实 Dapr/Kafka 门禁、不可变 `0.5.0-alpha.1` 发布及 CRM PR/main 固定版本消费闭环，状态为 `Frozen / Consumable`。P06 的 EF Outbox/Inbox、条件租约、幂等/顺序 checkpoint、DLQ/授权重放及 7/30/90 天保留已由真实 SQL Server 门禁、五个不可变 `0.6.0-alpha.1` 包及 CRM PR/main 固定版本消费闭环，状态为 `Frozen / Consumable`。P07 的 code-owned YARP route allowlist、外部身份头清理、按连接来源分区的固定窗口限流和 loopback E2E 已由五个不可变 `0.7.0-alpha.1` 包、exact-main 发布及 CRM PR/main 固定版本消费闭环，状态为 `Frozen / Consumable`；真实 CRM route、C01/C02/CRM03 与 P09 网络隔离仍未启用。P08 的 `0.8.0-alpha.1` 因 CRM 黑盒发现 BCL `HttpClient` 转发 baggage 而被取消消费者候选资格；trace-only 修复已作为五个不可变 `0.8.0-alpha.2` 包发布，CRM PR #33/#34/#35 已完成固定版本黑盒消费、证据绑定和提前状态的前向纠正。P08 当前为 `Published / Consumer Candidate`，等待 public S05 与 Platform S06 最终审计；未启用 runtime exporter/resilience，也未声明生产 SLO。

## 包边界

| 包 | 职责 | 当前状态 |
| --- | --- | --- |
| `CP6.Platform.Contracts` | 稳定的跨服务契约 | P02 snapshot；P03/P07 problems；P08 release/SLO evidence candidate |
| `CP6.Platform.Abstractions` | 平台抽象接口 | Request context；P08 telemetry/release abstractions |
| `CP6.Platform.AspNetCore` | ASP.NET Core 集成 | Auth/problems/gateway；P08 observability/health/resilience candidate |
| `CP6.Platform.Messaging` | 消息基础设施 | P04/P05 events/transport；P08 trace/metric candidate |
| `CP6.Platform.EntityFramework` | EF Core 集成 | P06 transactional messaging；P08 observer-only telemetry candidate |
| `CP6.Platform.Testing` | 仓库内测试支持 | `IsPackable=false`；仅 Test/CI 故障注入；不进入生产包集合 |

## 本地验证

```powershell
pwsh ./eng/verify.ps1 -Gate Format
pwsh ./eng/verify.ps1 -Gate Build
pwsh ./eng/verify.ps1 -Gate Unit
pwsh ./eng/verify.ps1 -Gate Integration
pwsh ./eng/verify.ps1 -Gate Integration -Profile p06-real
pwsh ./eng/verify.ps1 -Gate E2E
pwsh ./eng/verify.ps1 -Gate Contract
pwsh ./eng/verify.ps1 -Gate Security
```

每个 Gate 都在 `artifacts/verify/<gate>/` 输出机器可读的 `summary.json` 和 `results.junit.xml`。当前无适用实现的 Gate 必须返回 `NotApplicable`，不能静默跳过。

更多信息见 [P08 Observability and Resilience](docs/P08-OBSERVABILITY-RESILIENCE.md)、[P08 Publication](docs/P08-PUBLICATION.md)、[P07 Gateway](docs/P07-YARP-GATEWAY.md)、[P07 Publication](docs/P07-PUBLICATION.md)、[P06 Transactional Messaging](docs/P06-OUTBOX-INBOX.md)、[P06 Publication](docs/P06-PUBLICATION.md)、[P05 Dapr/Kafka](docs/P05-DAPR-KAFKA.md)、[P05 Publication](docs/P05-PUBLICATION.md)、[P04 CloudEvents](docs/P04-CLOUD-EVENTS.md)、[P04 Publication](docs/P04-PUBLICATION.md)、[P03 Authentication and Problem Details](docs/P03-AUTH-PROBLEM-DETAILS.md)、[P02 Request Context](docs/P02-REQUEST-CONTEXT.md)、[P01 Foundation](docs/P01-FOUNDATION.md)、[ADR-P01](docs/adr/ADR-P01-PACKAGE-SOURCE.md)、[Testing](TESTING.md) 和 [Contributing](CONTRIBUTING.md)。
