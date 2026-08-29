# Testing and verification

`eng/verify.ps1` 是本地与 CI 共用的验证入口。成功、失败和不适用都必须产生相同结构的机器证据。

| Gate | 当前行为 |
| --- | --- |
| `Format` | 校验 `.NET` 格式，不修改源码 |
| `Build` | restore 后以 Release 构建整个 solution |
| `Unit` | 运行基础约束、P02/P03、P04/P05 和 P06 的纯逻辑及负向矩阵 |
| `Contract` | 运行依赖/包架构测试，对五个非空运行时包执行两次打包并比较消费载荷逐项哈希，同时核对 P04 bundle 资产 |
| `Security` | 以 `NuGetAuditMode=all` + warnings-as-errors 失败关闭，并使用 nuget.org 漏洞数据检查直接与传递依赖 |
| `Integration` | 验证 ASP.NET 认证/上下文，以及 P07 loopback YARP 路由、伪造身份头清理、后端独立认证和限流；`p05-real`/`p06-real` 另跑真实 Dapr/Kafka/SQL |
| `E2E` | 运行 P07 loopback Gateway 到独立 Kestrel 目标的路由、header、直连和 429 Problem Details 门禁 |
| `Performance` | `NotApplicable`，P08 才冻结系统性能和韧性阈值 |
| `Migration` | `NotApplicable`，P07 不含数据库 Schema；消费方拥有 P06 migrations |

示例：

```powershell
pwsh ./eng/verify.ps1 -Gate Contract -Profile ci
```

输出：

- `artifacts/verify/contract/summary.json`
- `artifacts/verify/contract/results.junit.xml`

`summary.json` 的 `status` 仅允许 `Passed`、`Failed`、`NotApplicable`。失败 Gate 退出码非零；不适用 Gate 退出码为零且 JUnit 中有明确 skipped reason。

`Unit` 还会用隔离的假 `dotnet` 命令验证失败 Gate 必须返回非零并产生 `Failed` JSON/JUnit failure，同时验证 `NotApplicable` 必须返回零并产生带原因的 JSON/JUnit skipped。CI 无论成功或失败都上传各操作系统的 Gate 证据，保留 7 天。
