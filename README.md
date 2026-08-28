# CP6.Platform

CP6 分布式服务的共享契约与基础设施包。本仓库是 Platform 包的唯一生产源；业务服务只通过版本化 NuGet 包消费，不复制实现源码。

本地需要安装 .NET 8 SDK；`global.json` 允许在 .NET 8 内滚动到已安装的最新 feature band，但不会静默改用更高主版本。

P01 已建立可验证的仓库和包边界；P02 已交付只读 RequestContext。P03 进一步交付严格 RS256/JWKS 验证、RFC 9457 Problem Details 和 correlation 边界；不可变 `0.3.0-alpha.1` 已发布并通过 CRM PR/main 固定版本消费验证，状态为 `Frozen / Consumable`。P04-S01 增加 CloudEvents 1.0 structured JSON、Draft 2020-12 验证和内容寻址 contract bundle；`0.4.0-alpha.0` 仍是未发布的验证候选。

## 包边界

| 包 | 职责 | 当前状态 |
| --- | --- | --- |
| `CP6.Platform.Contracts` | 稳定的跨服务契约 | P02 RequestContext snapshot；P03 Problem definition/profile |
| `CP6.Platform.Abstractions` | 平台抽象接口 | P02：只读 `IRequestContext` / accessor |
| `CP6.Platform.AspNetCore` | ASP.NET Core 集成 | P02 request context；P03 RS256/JWKS、Problem Details、correlation |
| `CP6.Platform.Messaging` | 消息基础设施 | P04-S01：CloudEvents、JSON Schema、bundle 与兼容验证；未发布 |
| `CP6.Platform.EntityFramework` | EF Core 集成 | 边界已建立 |
| `CP6.Platform.Testing` | 消费方测试支持 | 边界已建立 |

## 本地验证

```powershell
pwsh ./eng/verify.ps1 -Gate Format
pwsh ./eng/verify.ps1 -Gate Build
pwsh ./eng/verify.ps1 -Gate Unit
pwsh ./eng/verify.ps1 -Gate Integration
pwsh ./eng/verify.ps1 -Gate Contract
pwsh ./eng/verify.ps1 -Gate Security
```

每个 Gate 都在 `artifacts/verify/<gate>/` 输出机器可读的 `summary.json` 和 `results.junit.xml`。当前无适用实现的 Gate 必须返回 `NotApplicable`，不能静默跳过。

更多信息见 [P04 CloudEvents](docs/P04-CLOUD-EVENTS.md)、[P03 Authentication and Problem Details](docs/P03-AUTH-PROBLEM-DETAILS.md)、[P02 Request Context](docs/P02-REQUEST-CONTEXT.md)、[P01 Foundation](docs/P01-FOUNDATION.md)、[ADR-P01](docs/adr/ADR-P01-PACKAGE-SOURCE.md)、[Testing](TESTING.md) 和 [Contributing](CONTRIBUTING.md)。
