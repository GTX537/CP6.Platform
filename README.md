# CP6.Platform

CP6 分布式服务的共享契约与基础设施包。本仓库是 Platform 包的唯一生产源；业务服务只通过版本化 NuGet 包消费，不复制实现源码。

本地需要安装 .NET 8 SDK；`global.json` 允许在 .NET 8 内滚动到已安装的最新 feature band，但不会静默改用更高主版本。

P01 只建立可验证的仓库、包边界、依赖方向、发布约定和 CI 基线，不交付运行时能力，也不发布空包。第一个可消费的 alpha 包从 P02 开始。

## 包边界

| 包 | 职责 | P01 状态 |
| --- | --- | --- |
| `CP6.Platform.Contracts` | 稳定的跨服务契约 | 边界已建立 |
| `CP6.Platform.Abstractions` | 平台抽象接口 | 边界已建立 |
| `CP6.Platform.AspNetCore` | ASP.NET Core 集成 | 边界已建立 |
| `CP6.Platform.Messaging` | 消息基础设施 | 边界已建立 |
| `CP6.Platform.EntityFramework` | EF Core 集成 | 边界已建立 |
| `CP6.Platform.Testing` | 消费方测试支持 | 边界已建立 |

## 本地验证

```powershell
pwsh ./eng/verify.ps1 -Gate Format
pwsh ./eng/verify.ps1 -Gate Build
pwsh ./eng/verify.ps1 -Gate Unit
pwsh ./eng/verify.ps1 -Gate Contract
pwsh ./eng/verify.ps1 -Gate Security
```

每个 Gate 都在 `artifacts/verify/<gate>/` 输出机器可读的 `summary.json` 和 `results.junit.xml`。当前无适用实现的 Gate 必须返回 `NotApplicable`，不能静默跳过。

更多信息见 [P01 Foundation](docs/P01-FOUNDATION.md)、[Testing](TESTING.md) 和 [Contributing](CONTRIBUTING.md)。
