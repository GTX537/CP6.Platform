# Testing and verification

`eng/verify.ps1` 是本地与 CI 共用的验证入口。成功、失败和不适用都必须产生相同结构的机器证据。

| Gate | 当前行为 |
| --- | --- |
| `Format` | 校验 `.NET` 格式，不修改源码 |
| `Build` | restore 后以 Release 构建整个 solution |
| `Unit` | 运行基础约束、P02-P07 回归及 P08 release/telemetry/SLO/messaging/fault 的纯逻辑和负向矩阵 |
| `Contract` | 运行依赖/包/文档架构测试；两次打包五个 runtime 与五个 symbol 包，比较逐项哈希并核对 P04/P08 资产所有权和内容安全 |
| `Security` | 以 `NuGetAuditMode=all` + warnings-as-errors 失败关闭，并使用 nuget.org 漏洞数据检查直接与传递依赖 |
| `Integration` | 验证 ASP.NET、P07 gateway 和 P08 health/resilience；`p05-real`/`p06-real` 另跑真实 Dapr/Kafka/SQL |
| `E2E` | 运行 P07 gateway 与 P08 两服务 W3C trace、exporter isolation、fault/cancellation/retry/circuit 门禁 |
| `Performance` | `NotApplicable`，P08-S01 冻结合同与边界，不声明系统负载/容量阈值 |
| `Migration` | `NotApplicable`，P08 不含 Schema 变更；消费方继续拥有 P06 migrations |

示例：

```powershell
pwsh ./eng/verify.ps1 -Gate Contract -Profile ci
```

输出：

- `artifacts/verify/contract/summary.json`
- `artifacts/verify/contract/results.junit.xml`

`summary.json` 的 `status` 仅允许 `Passed`、`Failed`、`NotApplicable`。失败 Gate 退出码非零；不适用 Gate 退出码为零且 JUnit 中有明确 skipped reason。

`Unit` 还会用隔离的假 `dotnet` 命令验证失败 Gate 必须返回非零并产生 `Failed` JSON/JUnit failure，同时验证 `NotApplicable` 必须返回零并产生带原因的 JSON/JUnit skipped。CI 无论成功或失败都上传各操作系统的 Gate 证据，保留 7 天。

`CP6.Platform.Testing` 只服务仓库测试，不能打包或成为生产项目依赖。其 `Cp6TelemetryRecorder` 提供线程安全的拓扑/标签/敏感数据断言，`Cp6HttpFaultScript` 提供确定性 outcome 序列，`AddCp6HttpFaultInjection` 只接受精确 `Test`/`CI` 环境。真实 P05/P06 profile 需要可用容器引擎；CI 中两个 profile 是独立且不可跳过的 job。
