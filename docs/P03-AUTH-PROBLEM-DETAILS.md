# P03 — RS256/JWKS、Problem Details 与 Correlation

| 项目 | 值 |
| --- | --- |
| 公开里程碑 | `P03` |
| 仓库交付版本 | `0.3.0.0` / package metadata `0.3.0-alpha.1` |
| 状态 | Implemented / Publication Pending |
| 前置 | P01；P02 继续向后兼容 |
| 生产包 | `CP6.Platform.Contracts`、`CP6.Platform.Abstractions`、`CP6.Platform.AspNetCore` |
| 消费证明 | 合并及发布后由 `GTX537/CP6.CRM` 固定版本验证 |

仓库交付版本使用四段 `VERSION`：`0.3.0.0`。NuGet alpha 使用 `0.3.0-alpha.1`，三个包以同一版本发布，禁止 `--skip-duplicate`。P03 不签发 Token、不创建 Identity Provider、不接入真实用户，也不实现 Gateway；C01 负责 RS256 issuer/Discovery/JWKS，P07 负责 Gateway。

## 1. 可消费合同

### 1.1 RS256/JWKS

消费者通过 `AddCp6JwtBearer(Cp6JwtBearerProfile)` 注册唯一验证边界：

- metadata 默认必须使用 HTTPS；测试环境只有显式 `RequireHttpsMetadata=false` 才可使用 HTTP；
- 只接受 `RS256`，要求签名、`kid`、配置的 issuer 和至少一个精确 audience；
- 需要 `iss`、`aud`、`sub`、`tenant_id`、`jti`、`iat`、`nbf`、`exp`；
- `tenant_id` 必须是 non-empty UUID；单值 claim 重复、NumericDate 非法或 `iat/nbf > exp` 均失败关闭；
- clock skew 只能配置在 0～5 分钟；默认 1 分钟；
- `MapInboundClaims=false`，下游只能读取原始公共 claim 名称；
- `RefreshOnIssuerKeyNotFound=true`。未知 `kid` 触发受控 JWKS refresh；刷新完成前当前请求失败，后续请求才可使用新 key；
- JWKS 缓存、自动刷新和 Last Known Good 生命周期复用 Microsoft IdentityModel，不由业务服务复制缓存实现。未过期旧 key 可继续验证；新 key 在刷新失败时不能被猜测或默认接受。

P03 的验证器不信任 body/query/cookie 或外部 `X-User-*` / `X-Tenant-*` header，也不把 Permission/DataScope 放入 Token。后续 CRM03 仍须检查用户、租户、权限和撤销投影。

### 1.2 RFC 9457 profile

`Cp6ProblemDefinition` 固定安全机器字段：absolute HTTPS `type`、安全 `title`、4xx/5xx `status`、大写下划线 `code` 和 `<product>.error.<key>`。`WriteCp6ProblemAsync` 输出 `application/problem+json`，并自动加入：

| 扩展 | 合同 |
| --- | --- |
| `code` | 稳定机器分支；Platform 预置 `CP6_AUTHENTICATION_REQUIRED` / `CP6_FORBIDDEN` |
| `messageKey` | 本地化资源键，不是已格式化文案 |
| `traceId` | 32 位小写 W3C trace-id |
| `correlationId` | 1～128 个批准字符；不包含用户输入或 PII |
| `errors` | 可选；只供 400/422 字段错误，不回显原始输入 |

默认输出不包含 exception、stack、Token、Cookie、SQL、Secret、请求 body 或 PII。业务服务可以定义自己的安全 code registry；CRM 仍要求 `CRM_*` 和 `crm.error.*`。

### 1.3 Correlation

`UseCp6Correlation()` 必须位于 authentication、request context 和业务 middleware 之前。它只接受唯一且符合 `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$` 的 `X-Correlation-Id`；缺失、多值或非法输入会生成新的 32 位小写 ID。最终 ID 写入 `HttpContext.TraceIdentifier` 和响应 header，供 Problem Details、Trace 与后续事件原样传播。

## 2. 失败关闭矩阵

| 情况 | 结果 |
| --- | --- |
| `alg=none`、HS256 或其他算法 | 401 |
| 无签名、无 `kid`、未知 `kid` 且 refresh 尚未取得新 key | 401 |
| issuer/audience 不匹配 | 401 |
| `exp` 缺失/过期、`nbf` 未到、时间 claim 非 NumericDate | 401 |
| `sub`、`tenant_id`、`jti`、`iat`、`nbf` 或 `exp` 缺失 | 401 |
| `tenant_id=Guid.Empty` 或重复单值 claim | 401 |
| 已认证但策略拒绝 | 403 |
| 非法/多值 correlation header | 生成安全新 ID，不回显非法值 |

401/403 challenge 使用相同 Problem Details shape。错误 body 不说明究竟是 issuer、audience、kid、claim 还是签名失败，详细原因只进入受控服务器日志。

## 3. 验证证据

P03 自动测试覆盖：

- 正常 RS256 Token 与全部必需 claims；
- 缺失 claim 矩阵、empty tenant、HS/RS confusion；
- unknown kid 触发一次 refresh，刷新前失败、刷新后使用新 key 成功；
- HTTPS metadata、issuer/audience、clock-skew 配置门禁；
- RFC 9457 content type、稳定扩展、trace/correlation 格式和敏感内容负向检查；
- correlation 合法传播与非法输入替换；
- 三包可重复 pack、NuGet dependency、架构方向和漏洞审计。

P03 以 Platform PR/main 双平台 CI、不可变 `0.3.0-alpha.1` 包 SHA-256、发布 workflow 和 CRM producer/consumer locator 共同关闭。发布前状态不得写成 Consumable。

## 4. 实施顺序

```csharp
builder.Services.AddCp6JwtBearer(new Cp6JwtBearerProfile
{
    Authority = configuration["Identity:Authority"]!,
    Issuer = configuration["Identity:Issuer"]!,
    Audiences = ["CP6.Web"]
});

app.UseCp6Correlation();
app.UseAuthentication();
app.UseAuthorization();
```

生产配置只能引用受保护配置中的 issuer/authority；包内不保存 Secret、公钥副本、真实 tenant/user 或环境 URL。C01 发布真实 Discovery/JWKS 且 CRM03/C01/C02 前置关闭之前，这段集成不得被描述为登录完成。
