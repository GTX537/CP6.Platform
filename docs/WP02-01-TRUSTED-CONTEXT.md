# WP02-01：可信租户上下文

本任务在 Platform 提供独立版本的 `ctx-binding-v1` 能力：每次 HTTP、消息或后台调用，先由宿主认证真实来源，再读取当前权威登记，校验租户、issuer、actor、effectiveActor、用途与最小范围，最后在回调期间暴露上下文。完成、失败或取消均销毁本次 scope。原七字段仍为 `ctx-v1`，未改字段、签名或序列化形状。

这次交付是库实现和本地测试。没有发布新包，没有在 Main/CRM 启用，没有数据库路由、建库、迁移或生产部署；真实宿主、PostgreSQL 和最终业务门的未验收范围见[验收责任映射](development/evidence/WP02-01/ACCEPTANCE.md)。测试中的固定身份/登记是 fixture，不代表生产认证。

## 可信边界

| 边界 | 必须提供的真实事实 | 不能当作证明的输入 |
|---|---|---|
| `IContextSourceVerifierV1<TInput>`，宿主实现 | 已认证 principal/profile/session，或已验证的 transport/producer/job 来源；原合同校验；当前执行服务身份；实际操作的租户、audience、用途和所需范围 | 请求 body/header、浏览器 tenant/issuer、事件 trace、旧 snapshot、序列化 evidence、历史 requester |
| `IContextBindingAuthorityV1`，IAM/宿主适配器实现 | 针对本次来源和操作读取当前应用登记、撤销状态、租户 issuer/region、actor/effectiveActor、有效期、最小 scope、配置版本；公开路径须精确匹配来源操作 | 调用者提交的登记、副本过期后继续放行、只核签名不核租户 issuer、默认租户/管理员 |
| `TrustedContextExecutorV1`，Platform | 检查两侧事实一致，复制集合，建立不可变上下文和独立 evidence，在 finally 清理 scope | 不替代 JWT 密码学验证、Owner 授权、数据库映射或提交事务中的最终门 |

可信 verifier 必须由宿主配置、只接受其实际入口的认证方案。HTTP 可读取**已经由指定认证方案验证的** principal，不能只凭任意 `IsAuthenticated` 身份或任意 claim 即放行；JWT 算法、签名、key、issuer、audience、`typ`、必需 claim、时效和轮换继续使用既有 P03/profile。消息适配器须先按既有合同校验原字节及 envelope 租户，并验证实际 transport/producer 与当前服务登记。调用 `VerifyAsync` 本身不会完成这些认证。

`ContextOperationV1.SourceReference` 是已识别入口/路由/任务的引用，`AuthenticationReference` 是可核查的认证记录引用；二者都不是凭证。Authority 每次根据这两个引用及 verified actor 查当前数据。其 `PublicRouteAllowed` 决定只对本次精确操作有效，不能把全局匿名标志搬进来。`IsActive` 指**此操作绑定当前有效**，不是要求把 tenant 的全局暂停状态一刀切映射为所有历史读取禁止；历史只读与新效果的权限由 Owner/登记适配器按各自门判定。

所有引用应为不含 secret/token/PII 的 opaque ID，不能塞入完整 token、授权串、个人姓名或敏感 body。Platform 不记录输入对象，不产生含这些数据的错误正文；宿主仍须审查自己的日志。Evidence 含身份信息，应按审计权限存取，不能整对象无差别写入公共日志或指标标签。

## 校验与版本

- 同一个非空 TenantId 必须同时等于 OrganizationId、操作预期租户、actor/effectiveActor 租户和当前登记租户。人类 UserId 保留旧合同可空语义；Service/Public 不伪造 UserId。
- issuer 必须是合法绝对 HTTPS URI，无 userinfo、query、fragment、控制字符；实际绑定按原字符串精确比较，不用大小写或末尾斜杠归一化扩大等价范围。region 仅接受该权威登记中的值；库没有另造 region 目录。
- host/profile、audience、purpose、entry kind、actor 与 effectiveActor 精确匹配当前登记。不同 actor/effectiveActor 需要当前 `PermissionDecisionRef`。Public 只允许显式 HTTP 路径，不可委托成人类/服务。Message/Background 的当前 actor 和 effectiveActor 都必须是 Service。
- 授予范围只有 `RequiredScopes ∩ authenticated scopes ∩ registered scopes`；任何 required scope 缺失即拒绝，空 required scopes 不授予权限。不会识别隐含 admin 或 wildcard。输入及证据中的 scope 集合独立只读，原集合后续变化不能提高已解析权限。
- 在异步 authority 返回之后检查认证和登记的有效期，再调用业务处理。证据只是该时点的解析记录；后续撤销、授权变化和数据库映射变化仍必须在 Owner 最终门识别。
- 新 evidence 有独立 `CapabilityVersion=ctx-binding-v1`、`ContextContractVersion=ctx-v1`、resolution GUID、认证时间/校验时间、登记引用/版本、来源/操作引用、actor/effectiveActor 和授予范围。它不能被反序列化后直接建立可信 scope；`TrustedRequestContextV1` 没有公开构造器。
- 旧 Snapshot/IRequestContext/IRequestContextResolver 原七字段完全保留，没有新增必需 issuer/region/epoch。Subject/Audience/CorrelationId 不新增旧合同长度上限或改写字符串，TokenId 空白仍转 null。原 CloudEvent 字段和原字节处理未修改。

## HTTP 采用

旧宿主继续使用 `AddCp6RequestContext<TResolver>()` / `UseCp6RequestContext()`：原 resolver 生成的合法 snapshot 可读，缺失/非法 snapshot 仍为 403；它**不产生**可信绑定 evidence。既有 JWT 认证失败 401 语义不改。现有实现本就使用 scoped accessor 和 finally；本次增加可嵌套、可并发调用的显式 scope，不宣称曾有 AsyncLocal 缺陷。

新宿主在实现和验证自己的两个适配器后显式选择新入口：

```csharp
using CP6.Platform.AspNetCore.Context;

// HostSourceVerifier / CurrentRegistrationAuthority 是宿主自己的生产实现。
builder.Services.AddCp6TrustedRequestContextV1<HostSourceVerifier, CurrentRegistrationAuthority>();
app.UseAuthentication();
app.Map("/health", branch => branch.Run(context => Task.CompletedTask));
app.UseCp6TrustedRequestContextV1();
// 后续为本宿主既有授权与业务入口。
```

同一个业务 pipeline 选择一个 context middleware，避免重复包裹。两套 DI 注册可以共存以便迁移，二者会使用同一个 scoped accessor；旧 scope 内 `ITrustedContextAccessorV1.CurrentTrusted` 为 null。业务不得把 null evidence 当作新版已验证。

新入口只将**业务回调开始之前**的 `ContextResolutionExceptionV1` 映射为：AuthenticationRequired→401、BindingRejected→403、AuthorityUnavailable→503，正文不含输入信息。业务抛出的同类型异常、未知适配器异常和取消继续传播给宿主既有处理层。没有广泛捕获异常并伪装为授权失败。

Tenantless 健康检查由宿主明确放在业务上下文路径之外，不制造 Guid.Empty、默认 tenant 或默认 schema。示例只展示 pipeline 分支；本次没有实际宿主连接计数或数据库探测证明。

## 消息与后台采用

`MessageContextExecutorV1<TDelivery>` 位于 Messaging，`BackgroundContextExecutorV1<TJob>` 位于 Abstractions，均不依赖 AspNetCore。宿主使用一个明确的 DI scope 为每次投递/工作尝试取得 executor、verifier 和 accessor，调用：

```csharp
await messageExecutor.ExecuteAsync(nativeDelivery, async ct =>
{
    // 此处读取 IRequestContextAccessor / ITrustedContextAccessorV1。
    // 仍执行既有内容门、Owner 授权和同一事务最终门。
    await owner.HandleAsync(nativeDelivery, ct);
}, cancellationToken);
```

后台执行同样使用 `backgroundExecutor.ExecuteAsync(job, handler, cancellationToken)`。每次重试都重新调用 verifier 和当前 authority；不能把第一次 HTTP scope、旧证据或重试前认证结果永久缓存为执行许可。`OriginalRequesterReference` 仅作历史审计，不能覆盖当前服务的 Subject/UserId 或 scope。库不重写 delivery/event 原字节、不跳过 S6 内容门、不对 Duplicate/IgnoredOutOfOrder 或 typed replay 作业务成功决定。

需要无 HTTP 的注册时，宿主在同一个作用域注册 `ContextScopeAccessorV1`，将 `IRequestContextAccessor` 和 `ITrustedContextAccessorV1` 都指向该实例，再注册真实 authority/verifier 和 executor。不要把 scoped authority/resolver 装入单例，或另造一个 accessor 供业务读取。

## Scope 生命周期

进入 scope 先放置空 frame，在异步认证/登记期间屏蔽外层身份。成功后在回调内暴露上下文；finally 撤销共享 holder，并恢复当前调用流先前的 frame。并行 async 调用拥有不同 frame，嵌套 A/B/A 恢复 A，捕获该 frame 的 detached child 在 scope 结束后也读不到它。

业务可保留不可变对象作为审计快照，但它不等于仍然有效的授权；库没有“用旧结果恢复 scope”的接口。需要跨回调生命周期执行的任务必须经真实 verifier/authority 建立自己的新 scope。不要用 `async void`、丢弃 Task 或共享业务 DbContext；这些不是可持久化执行模型。

## 宿主采用与依赖清单

| 层/入口 | 本次实际状态 | 依赖与后续责任 |
|---|---|---|
| Platform 库 / HTTP fixture | 源码实现；本地测试。没有可运行生产宿主 | AspNetCore→Abstractions/Contracts，沿用原依赖；适配器由宿主实现 |
| Platform 库 / Message fixture | 本地测试；原消息合同/字节不改 | Messaging→Abstractions/Contracts；实际 transport 验证及每个 consumer 采用由 Owner 留证 |
| Platform 库 / Background fixture | 本地测试；当前服务/重试来源隔离 | Abstractions→Contracts；实际 job 登记、调度身份、最终门由宿主留证 |
| Main，源码 pin `e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47` | 固定源码搜索未发现 Add/UseCp6RequestContext；不声称已注册/启用/运行新版 | 原 TenantMiddleware/default fallback 不在本任务改动；真实可信映射、issuer/region 和 PostgreSQL 路由须单独采用 |
| CRM browser / sidecar / Main service | 未在本任务重验当前 CRM 源码、包或运行宿主；均为 NOT_RUN | 不更换既有不同 profile；逐入口记录其真实认证方案及权威映射 |
| 包、部署、PostgreSQL、业务写入 | 新包未发布；宿主注册/启用/运行/业务验收 NOT_RUN | 不将本地项目引用、fixture 或库测试作为生产采用 |

逐入口采用记录必须包含 app/host/repo full SHA、实际包版本与 digest、resolver/verifier/authority、认证 scheme/profile、context/capability version、tenant scope、registration ref/version、public route allowlist、middleware 顺序、DB routing、历史读取政策、最终门及所有 writer 清单、启用配置、runtime identity、观测时间和证据。包引用、注册、启用、实际运行、业务通过分别记录；不能合并为一个“已采用”。

数据库唯一后续目标为 PostgreSQL。依照 `cp6-pg-tenancy-v1` 的可信目录/租户映射、连接池事务上下文和权限设计采用，TenantId 与授权约束继续存在。Context 不返回 schema 字符串，也不读取调用者的 schema 参数。本次不证明 TenantId→schema 路由、连接获取前拒绝、池回收隔离、RLS 或同事务最终门已经接入。

精确源码、输入、验证计数、失败保留和交付固定提交见本任务 [evidence](development/evidence/WP02-01/PLAN.md)。
