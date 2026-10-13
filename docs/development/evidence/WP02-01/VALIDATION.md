# WP02-01 本地验证

工作树基线 `dc4c0ab216974339ab3c2cebaaf4bb92c9e9b351`，任务分支 `codex/wp02-01-trusted-context-20261012`。测试时源码及项目文件的逐文件 SHA256、恢复依赖图及实际本地程序集 hash 见 [full-test-inputs.json](full-test-inputs.json)。该清单标识修改后的实际工作树字节；它没有把基线 SHA 冒称实现 SHA。最终提交由 DELIVERY/交接固定。

环境：Windows 10.0.26200 / win-x64；.NET SDK 8.0.424、MSBuild 17.11.48、Microsoft.NETCore.App / AspNetCore.App 8.0.30、VSTest 17.11.1。现有项目依赖 restore 成功，无 csproj/依赖/版本/锁文件变化。执行器为 `C:/Users/tt/.dotnet/dotnet.exe`，未使用远程 Actions。

## 必需完整项目验证

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --logger 'trx;LogFileName=full-http.trx' --results-directory docs/development/evidence/WP02-01/test-results
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --logger 'trx;LogFileName=full-unit.trx' --results-directory docs/development/evidence/WP02-01/test-results
```

| 执行 | 发现/执行 | 通过 | 失败 | 跳过 | 退出码 |
|---|---:|---:|---:|---:|---:|
| [AspNetCoreTests](test-results/full-http.trx) | 243 | 243 | 0 | 0 | 0 |
| [UnitTests](test-results/full-unit.trx) | 259 | 259 | 0 | 0 | 0 |

合计 502 条本地测试。日志为 full-http.log/full-unit.log，TRX 原始开始/结束时间、计数和 hash 另见 [test-summary.json](test-summary.json)。完整项目结果包含未改的 JWT、消息、合同等既有回归；这不等于实际 Main/CRM 或 PostgreSQL 宿主验收。当前源码及依赖不再变化时，这次成功结果按仓库策略复用，不因提交/合并 SHA 变化重复同一套测试。

## 开发期失败与针对性验证（全部保留）

所有过滤执行都使用对应项目的 `dotnet test --configuration Release --no-restore`，附同目录 `--logger trx;LogFileName=<name>.trx` 和 `--results-directory`。

| TRX/日志名 | filter | 实际结果与原因 |
|---|---|---|
| red-bindings | `FullyQualifiedName~Context.TrustedContextTests` | 10 发现，0 通过/10 失败/0 跳过；编译成功后执行到未实现 executor，实际正反断言失败 |
| red-scope | `FullyQualifiedName~UnitTests.Context` | 23 发现，0 通过/23 失败/0 跳过；加入 scope 隔离/清理断言，仍为未实现 seam |
| green-core | 同上 | 23 通过/0 失败/0 跳过；实现绑定与 scope 后 |
| red-http | `FullyQualifiedName~Context.TrustedContextMiddlewareTests` | 9 发现，1 通过/8 失败/0 跳过；健康分支不进入 middleware，其他断言因新 HTTP seam 未实现而失败 |
| red-legacy | `FullyQualifiedName~RequestContextMiddlewareTests` | 7 发现，6 通过/1 失败/0 跳过；新增嵌套恢复要求尚未实现。原非嵌套清理本来通过 |
| green-http | `FullyQualifiedName~RequestContextMiddlewareTests\|FullyQualifiedName~Context.TrustedContextMiddlewareTests` | 16 通过/0 失败/0 跳过；新 HTTP adapter 和兼容 scope 完成 |
| boundary-check | `FullyQualifiedName~UnitTests.Context` | 70 发现，69 通过/1 失败/0 跳过；URI parser 接受含空格 issuer。补 IsWellFormedOriginalString 后由 full-unit 对应原断言通过覆盖 |

`red-source.json` 保留首次实现前新增 Context 源码/测试 hash。RED 为可编译且实际发现用例的失败，未把编译失败、零发现或跳过算作证据。最后增加 pending authority cancellation、异步登记返回后 expiry 检查；完整 UnitTests 中的新 Context 行为共 72 行，全部通过。

## 其他核对及边界

- 11 份固定来源（含原设计、Main/Platform Git 字节、admission 和 S01 固定包）hash 核对，见 PROVENANCE.json。
- 原 28 AC 与 PGT-004 原文/state 全保留；本地匹配 TRX 子断言单独标注，见 ACCEPTANCE.md/acceptance-map.json。直接分配固定向量 0 条，没有冒充 247 场景或完整目标验收。
- 新类型、旧三文件、局部测试及任务文档均处于分配窗口；原 Snapshot/IRequestContext/IRequestContextResolver、JWT、CloudEvent、项目依赖、EF、工作流未修改。
- 没有真实认证/登记服务、数据库、包发布、实际宿主、UI 切组织、业务最终门、部署或发布候选验收。Scope 与 evidence 不能代替这些责任。
- 任务级完整 diff 审查结果及最终推送触发检查另记 REVIEW/DELIVERY；本文件只记录已实际执行验证。
