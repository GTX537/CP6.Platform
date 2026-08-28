# Testing and verification

`eng/verify.ps1` 是本地与 CI 共用的验证入口。成功、失败和不适用都必须产生相同结构的机器证据。

| Gate | 当前行为 |
| --- | --- |
| `Format` | 校验 `.NET` 格式，不修改源码 |
| `Build` | restore 后以 Release 构建整个 solution |
| `Unit` | 运行基础约束及 P02 只读/校验负向矩阵 |
| `Contract` | 运行依赖/包架构测试，只对三个 P02 非空包执行两次打包并比较消费载荷逐项哈希 |
| `Security` | 以 `NuGetAuditMode=all` + warnings-as-errors 失败关闭，并使用 nuget.org 漏洞数据检查直接与传递依赖 |
| `Integration` | 验证 ASP.NET 中间件建立/清理上下文、缺失/非法租户 403、伪造头无效 |
| `E2E` | `NotApplicable`，Platform 不是可独立运行应用 |
| `Performance` | `NotApplicable`，P02 未定义性能阈值 |
| `Migration` | `NotApplicable`，P02 没有数据库资产 |

示例：

```powershell
pwsh ./eng/verify.ps1 -Gate Contract -Profile ci
```

输出：

- `artifacts/verify/contract/summary.json`
- `artifacts/verify/contract/results.junit.xml`

`summary.json` 的 `status` 仅允许 `Passed`、`Failed`、`NotApplicable`。失败 Gate 退出码非零；不适用 Gate 退出码为零且 JUnit 中有明确 skipped reason。

`Unit` 还会用隔离的假 `dotnet` 命令验证失败 Gate 必须返回非零并产生 `Failed` JSON/JUnit failure，同时验证 `NotApplicable` 必须返回零并产生带原因的 JSON/JUnit skipped。CI 无论成功或失败都上传各操作系统的 Gate 证据，保留 7 天。
