# Testing and verification

`eng/verify.ps1` 是本地与 CI 共用的验证入口。成功、失败和不适用都必须产生相同结构的机器证据。

| Gate | P01 行为 |
| --- | --- |
| `Format` | 校验 `.NET` 格式，不修改源码 |
| `Build` | restore 后以 Release 构建整个 solution |
| `Unit` | 运行基础约束单元测试 |
| `Contract` | 运行依赖/包架构测试并两次打包比较 nuspec 与消费载荷的逐项哈希（NuGet 随机 OPC 元数据和容器时间戳不作为内容漂移） |
| `Security` | 以 `NuGetAuditMode=all` + warnings-as-errors 失败关闭，并使用 nuget.org 漏洞数据检查直接与传递依赖 |
| `Integration` | `NotApplicable`，P01 没有运行时集成 |
| `E2E` | `NotApplicable`，P01 没有可运行应用 |
| `Performance` | `NotApplicable`，P01 没有运行时路径 |
| `Migration` | `NotApplicable`，P01 没有数据库资产 |

示例：

```powershell
pwsh ./eng/verify.ps1 -Gate Contract -Profile ci
```

输出：

- `artifacts/verify/contract/summary.json`
- `artifacts/verify/contract/results.junit.xml`

`summary.json` 的 `status` 仅允许 `Passed`、`Failed`、`NotApplicable`。失败 Gate 退出码非零；不适用 Gate 退出码为零且 JUnit 中有明确 skipped reason。

`Unit` 还会用隔离的假 `dotnet` 命令验证失败 Gate 必须返回非零并产生 `Failed` JSON/JUnit failure，同时验证 `NotApplicable` 必须返回零并产生带原因的 JSON/JUnit skipped。CI 无论成功或失败都上传各操作系统的 Gate 证据，保留 7 天。
