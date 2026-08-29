# P07-S01 — YARP Gateway、路由、Header 清理与限流

| 项目 | 内容 |
| --- | --- |
| 决策状态 | Accepted implementation scope |
| 实现状态 | Frozen / Consumable |
| 版本 | `0.7.0.0` / package metadata `0.7.0-alpha.1` |
| 前置 | P03 RS256/JWKS、Problem Details 与 correlation |
| 生产包 | `CP6.Platform.AspNetCore` 与兼容包族 |
| 日期 | 2026-08-29 |

仓库交付版本使用四段 `VERSION`：`0.7.0.0`，不可变候选 package metadata `0.7.0-alpha.1`。P07-S01 为 CP6 的外部 HTTP 入口提供可复用 YARP 合同。它固定 allowlist 路由、目标地址验证、外部身份头清理和按连接来源分区的限流。它不签发身份、不把 Gateway 变成最终授权点，也不通过一个可伪造的“已验证”header 让后端跳过 JWT。

## 1. 冻结合同

### 1.1 Route 与 destination

- 消费方用 `Cp6GatewayProfile` 显式登记 route、cluster 和 destination；不登记的 path 返回 404，不转发到任意目标。
- route/cluster/destination ID 必须是长度不超过 63 的 lowercase DNS-style ID；重复 ID、未知 cluster、非法 path/method 在启动注册时失败。
- 每条 route 必须有固定窗口限流；窗口为 1 秒到 1 小时，permit 为 1 到 10000，queue 固定为 0，超限立即失败。
- destination 必须是无 userinfo/query/fragment 的绝对 HTTP(S) base URI；默认只接受 HTTPS。本地 loopback 测试必须显式 `RequireHttpsDestinations=false`。
- P07 允许 route 使用宿主已有的 ASP.NET Core authorization policy，但不定义 CRM 的身份、Function、DataScope 或 PII policy。

### 1.2 不可信 Header

在 YARP 建立下游请求后，P07 删除所有外部：

- `X-User` / `X-User-*`
- `X-Tenant` / `X-Tenant-*`
- `X-Organization` / `X-Organization-*`
- `X-CP6` / `X-CP6-*`
- `Forwarded` / `Forwarded-Client-Cert` / `X-Forwarded-Client-Cert`

Gateway 不从这些值建立 RequestContext。`Authorization`、`Cookie` 和 `X-Correlation-Id` 不属于身份注入列表；后端仍必须用 P03/C01/C02/CRM03 的独立信任链验证它们。YARP 自己生成的 `X-Forwarded-For/Host/Proto/Prefix` 只描述当前代理连接，调用方提供的同名值不能成为租户或用户权威。

### 1.3 限流失败

限流按 `HttpContext.Connection.RemoteIpAddress` 分区，不直接信任浏览器可控的 identity header。拒绝时：

- HTTP status 为 `429`；
- content type 为 `application/problem+json`；
- `code=CP6_RATE_LIMIT_EXCEEDED`、`messageKey=cp6.error.rateLimitExceeded`；
- 保留安全 correlation/trace；
- 有可用 lease metadata 时输出秒数 `Retry-After`；
- 请求不会抵达 destination，不产生下游副作用。

生产 edge 若需要把真实客户端地址写入 `RemoteIpAddress`，必须由宿主通过已知代理 allowlist 配置 ASP.NET Core Forwarded Headers。P07 不接受开放式 `KnownNetworks`，也不把原始 `X-Forwarded-For` 当成可信来源。

## 2. 使用顺序

```csharp
builder.Services.AddCp6Gateway(new Cp6GatewayProfile
{
    Routes =
    [
        new Cp6GatewayRoute
        {
            RouteId = "crm",
            ClusterId = "crm-web",
            MatchPath = "/crm/{**remainder}",
            RateLimit = new Cp6GatewayRateLimit
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1)
            }
        }
    ],
    Clusters =
    [
        new Cp6GatewayCluster
        {
            ClusterId = "crm-web",
            Destinations =
            [
                new Cp6GatewayDestination
                {
                    DestinationId = "crm-web-1",
                    Address = new Uri(configuration["Gateway:CrmWebAddress"]!)
                }
            ]
        }
    ]
});

app.UseRouting();
app.UseCp6Correlation();
app.UseCp6GatewayRateLimiting();
app.MapCp6Gateway();
```

地址来自受保护环境配置，不写入包、仓库或证据。Gateway 宿主若配置 authentication/authorization，必须把对应 middleware 放在 endpoint 执行前，并继续让 destination 独立复验所需身份。

## 3. 自动化验收

`GatewayContractTests` 在同一测试进程启动两个独立 Kestrel host，证明：

1. `/crm/{**remainder}` 只转发到登记的 CRM destination，未登记 `/portal/**` 返回 404 且 destination 调用次数为零；
2. 外部 identity/client-certificate/Forwarded 伪造值在 destination 不可见，Authorization 与 correlation 仍按协议到达；
3. 直接请求 destination 或经 Gateway 请求时，缺少 destination authentication 都返回 401；Gateway 不创造后端信任；
4. 超过 route limit 返回安全 429 Problem Details，destination 调用次数不增加；
5. HTTP production destination、未知 cluster、缺失/非法限流和不安全 path 在注册阶段失败。

验证入口：

```powershell
pwsh ./eng/verify.ps1 -Gate Integration -Profile ci
pwsh ./eng/verify.ps1 -Gate E2E -Profile ci
pwsh ./eng/verify.ps1 -Gate Contract -Profile ci
pwsh ./eng/verify.ps1 -Gate Security -Profile ci
```

## 4. 明确不做

- 不创建 Gateway 可执行仓库、容器、DNS、证书、Secret、云资源或 DEV/UAT/PROD 部署。
- 不提交 P09 的 NetworkPolicy、内部端口隔离、Dapr components 或环境 route 配置。
- 不实现 C01 issuer/Discovery/JWKS、C02 权限撤销事件、CRM03 最终授权或真实登录。
- 不启用 CRM `/crm/**`、公开站点、Worker、业务 API 或任何业务页面。
- 不声明直接 destination 已由网络封锁；S01 只证明即使直连，destination authentication 仍不能被 header 绕过。

## 5. 完成定义

S01 代码与跨平台门禁已合并，P07-S02 已从精确 Platform `main@329bf8ee82091de569cb80f1e83fc5d518f74068` 发布不可变 `0.7.0-alpha.1` 并保存包/artifact SHA-256。CRM PR #31 和 #32 已通过固定版本 route/header/rate-limit 消费与 locator 冻结门禁，公共 CP6 PR #71 及合并后五组门禁也已通过，因此 P07 状态为 `Frozen / Consumable`。

## 6. 完成证据

- Platform：实现 PR #15 / run 33260845830、实现 `main@8167deac14bf82b0f576e73bf7c2e202049d1896` / run 33261055327 attempt 2、发布 PR #16 / run 33262266953、发布 `main@329bf8ee82091de569cb80f1e83fc5d518f74068` / run 33262410890 和 exact-main publish run 33262569274 均通过。
- CRM：消费 PR #31 / run 33264347561、合并后 `main@02f7078de6a67e7f3fded6df6a84b9f6fb712a84` / run 33264676796、冻结 PR #32 / run 33265394681 与最终 `main@467d95e46625d4db0bb7aa0932aff5464f64a01b` / run 33265702772 均通过。
- 公共同步：`GTX537/CP6` PR #71 的六组 PR runs 全通过，合并为 `main@47263a498caadcb545092ca617e3d86633e9bea5`；exact-main runs 33266594792、33266594799、33266594815、33266594818、33266594824 全部成功。
- 冻结边界不变：当前证据不创建 Gateway 宿主、不登记真实 CRM route、不实现 C01/C02/CRM03，也不交付 P09 NetworkPolicy 或任何环境部署。
