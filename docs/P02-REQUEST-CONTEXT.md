# P02 — Read-only Request Context

| 项目 | 内容 |
| --- | --- |
| 决策状态 | Accepted |
| 实现状态 | Implementation Candidate；待 Platform `main` 验证、不可变包发布及 CRM 消费证明 |
| 仓库交付版本 | `0.2.0.0` / package metadata `0.2.0-alpha.1` |
| 范围 | Contracts + Abstractions + ASP.NET Core integration |
| 日期 | 2026-08-28 |

仓库交付版本使用四段 `VERSION`：`0.2.0.0`；对外候选包使用 SemVer `0.2.0-alpha.1`。

## 1. 目标与完成定义

P02 为 CP6 微服务提供统一、只读、不可由业务代码改写的请求身份上下文。它解决“当前操作属于哪个组织、哪个用户、哪个认证主体以及哪个关联链路”，并冻结“没有默认租户”的安全规则。

P02 完成必须同时满足：

1. 下述公开接口形状由反射测试精确约束；
2. 空 TenantId、空 Subject、空 Audience 或空 CorrelationId 均失败关闭；
3. ASP.NET 集成只接受消费服务注册的可信 resolver，不直接读取浏览器输入；
4. Platform Windows/Linux CI 通过；
5. 三个非空包以不可复用版本 `0.2.0-alpha.1` 发布；
6. `CP6.CRM` 固定该版本完成 restore、build 和最小运行验证。

在第 5、6 项取得证据前，状态保持 `Implementation Candidate`，不得写成已完成。

## 2. 冻结的公开契约

```csharp
public interface IRequestContext
{
    Guid TenantId { get; }
    Guid? UserId { get; }
    string Subject { get; }
    string Audience { get; }
    string CorrelationId { get; }
    string? TokenId { get; }
    bool IsPublic { get; }
}
```

- 所有属性只有 getter；消费方没有 setter，也不能替换 accessor 的 Current。
- `TenantId` 就是 CRM 的 `OrganizationId`，只是跨模块命名不同；必须为非空 UUID。
- `UserId` 可空，支持非用户主体，但不得自动创建“默认用户”。
- `Subject` 是认证系统提供的不透明主体标识，Platform 不解析业务含义。
- `Audience` 在 P02 只携带；真正的 token audience 校验属于 P03。
- `CorrelationId` 必须保留可信适配器提供的原始非空值，不在此层静默改写。
- `TokenId` 可空；空白值归一化为 null。
- `IsPublic` 只能由可信 resolver 分类；客户端声明无效。

公开请求也必须先由服务的可信 route repository 将 `siteKey` 解析到真实组织，再建立 `IsPublic=true` 且 TenantId 非空的上下文；公开响应不得因此暴露 TenantId/OrganizationId。

`RequestContextSnapshot` 是 resolver 与 Platform 的不可变交接数据；`RequestContext` 在进入业务管道前进行上述校验。

## 3. ASP.NET 数据流

```text
认证/网关已验证身份
        |
        v
服务自有 IRequestContextResolver
        |
        v
RequestContextMiddleware --无/非法--> HTTP 403
        |
        v
Scoped IRequestContextAccessor.Current
        |
        v
业务处理完成 -> finally 清空 Current
```

消费服务必须实现 `IRequestContextResolver`，从受信任的认证结果构造 snapshot。Platform 中间件本身不从 body、query、cookie、外部 `X-Tenant`、`X-User` 等浏览器可控输入推导身份。resolver 返回 null 或 snapshot 校验失败时，中间件返回 403 且不调用后续应用。

`IRequestContextAccessor` 是 scoped 服务，请求结束后由 `finally` 清空，避免同一作用域的后续逻辑误用旧值。

## 4. 无默认租户规则

以下情况一律不得回退到 A1、第一条组织、开发组织或任何固定 GUID：

- 管理端组织缺失、空值、未知、停用或区域不匹配；
- 用户与组织关系无法确认；
- 后台任务没有显式枚举 TenantId；
- 客户端只提供租户 header/query/cookie/body；
- resolver 异常地产生 `Guid.Empty`。

P02 中间件对“上下文不存在/非法”统一 403。需要用 404 隐藏资源存在性的业务场景由消费服务的授权/资源层处理。后台任务不经过本中间件时，必须从任务载荷或受信任调度记录显式取得 TenantId，再创建 `RequestContext`；创建失败就停止任务。

## 5. 消费方式

服务启动注册：

```csharp
builder.Services.AddCp6RequestContext<CrmRequestContextResolver>();
app.UseCp6RequestContext();
```

业务服务只注入只读 accessor：

```csharp
public sealed class CustomerService(IRequestContextAccessor contextAccessor)
{
    public Guid OrganizationId =>
        contextAccessor.Current?.TenantId
        ?? throw new InvalidOperationException("Request context is required.");
}
```

CRM 适配器只能进行 `TenantId` 到 `OrganizationId` 的命名映射，不能改变标识值。

## 6. 包与发布约束

P02 只发布包含真实运行时程序集的三个包：

- `CP6.Platform.Contracts` `0.2.0-alpha.1`
- `CP6.Platform.Abstractions` `0.2.0-alpha.1`
- `CP6.Platform.AspNetCore` `0.2.0-alpha.1`

`Messaging`、`EntityFramework`、`Testing` 仍只有 P01 边界，不发布空包。发布工作流只能从当前 `origin/main` 的明确 commit 手动触发，使用最小 `packages: write` 的 `GITHUB_TOKEN`。版本冲突必须失败，禁止 `--skip-duplicate`。正式 NuGet 签名仍按 P10 治理，不在 P02 偷跑。

`CP6.CRM` 的 Actions 必须获得这些私有包的 repository read access；本机则使用用户级安全凭据。任何 token、PAT 或密码不得写入仓库。

## 7. 验证矩阵

| Gate | P02 证据 |
| --- | --- |
| Unit | 公开接口精确 getter 集；值不改写；TenantId/必填字符串负向矩阵；可空 UserId/TokenId |
| Integration | resolver 成功；请求后清理；缺失 context 403；空 TenantId 403；伪造 X-Tenant 不生效 |
| Contract | 项目依赖方向；只允许 AspNetCore shared framework；三个非空包两次打包内容一致 |
| Security | 全传递依赖漏洞审计，warnings-as-errors |
| E2E | NotApplicable：Platform 不是独立应用，CRM 消费验证单独留证 |
| Performance | NotApplicable：P02 未定义性能验收阈值 |
| Migration | NotApplicable：P02 没有数据库资产 |

## 8. 后续边界

P02 不验证 JWT、JWKS、issuer、audience 或签名，不生成 ProblemDetails，也不决定 API 资源授权；这些属于 P03 及业务模块。P02 只承载已经由可信边界形成的数据并阻止缺失上下文继续执行。
